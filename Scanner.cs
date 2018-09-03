using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HtmlAgilityPack;
using Microsoft.Extensions.Configuration;

namespace DailyNewsFeed
{
    public class Scanner
    {
        readonly IConfigurationSection Configuration;
        readonly Storage Storage;
        readonly HttpClient Client;

        public Scanner(IConfigurationSection configuration, Storage storage, HttpClient client)
        {
            Configuration = configuration;
            Storage = storage;
            Client = client;
        }

        public async Task Process()
        {
            Console.WriteLine($"Processing {Configuration.Key}...");

            var dateTime = DateTimeOffset.UtcNow;

            foreach (var block in Configuration.GetSection("Blocks").GetChildren())
            {
                var uri = new Uri(block["Url"]);
                var document = await LoadItem(uri);
                await ProcessBlock(uri, dateTime, document, block);
            }
        }

        async Task ProcessBlock(Uri uri, DateTimeOffset dateTime, HtmlDocument document, IConfigurationSection configuration)
        {
            Console.WriteLine($"  Processing {configuration.Key}...");
            var blockNode = document.DocumentNode.SelectSingleNode(configuration["BlockSelector"]);
            var keyRegExp = new Regex(configuration["KeyRegExp"]);

            var stories = blockNode.SelectNodes(configuration["StorySelector"]);
            var seenStories = new HashSet<string>();
            var storyIndex = 0;
            foreach (var story in stories)
            {
                var key = keyRegExp.Match(GetHtmlValue(story, configuration.GetSection("KeySelector"))).Groups[1].Value;
                var url = new Uri(uri, GetHtmlValue(story, configuration.GetSection("UrlSelector")));
                var imageUrl = new Uri(uri, GetHtmlValue(story, configuration.GetSection("ImageUrlSelector")));
                var title = GetHtmlValue(story, configuration.GetSection("TitleSelector"));
                var description = GetHtmlValue(story, configuration.GetSection("DescriptionSelector"));

                if (seenStories.Contains(key))
                {
                    continue;
                }
                seenStories.Add(key);

                storyIndex++;
                await Storage.ExecuteNonQueryAsync("INSERT INTO Stories (Date, Site, Block, Position, Key, Url, ImageUrl, Title, Description) VALUES (@Param0, @Param1, @Param2, @Param3, @Param4, @Param5, @Param6, @Param7, @Param8)",
                    dateTime,
                    Configuration.Key,
                    configuration.Key,
                    storyIndex,
                    key,
                    url.ToString(),
                    imageUrl.ToString(),
                    title,
                    description
                );
            }

            Console.WriteLine($"    Collected {storyIndex} stories");
        }

        static readonly Regex WhitespacePattern = new Regex(@"\s+");

        static string GetHtmlValue(HtmlNode node, IConfigurationSection configuration)
        {
            foreach (var type in configuration.GetChildren())
            {
                switch (type.Key)
                {
                    case "Constant":
                        return type.Value;
                    case "Attribute":
                        string value = null;
                        foreach (var attribute in type.GetChildren())
                        {
                            value = node.SelectSingleNode(attribute.Value)?.Attributes?[attribute.Key]?.Value ?? value;
                        }
                        return WebUtility.HtmlDecode(value ?? $"<default:{type.Path}>");
                    case "InnerHtml":
                        return node.SelectSingleNode(type.Value).InnerHtml;
                    case "InnerText":
                        return WhitespacePattern.Replace(WebUtility.HtmlDecode(node.SelectSingleNode(type.Value)?.InnerText ?? $"<default:{type.Path}>"), " ").Trim();
                    case "ResponsiveImage":
                        var img = node.SelectSingleNode(type.Value);
                        var dataSrc = img.GetAttributeValue("data-src", "");
                        var dataWidths = img.GetAttributeValue("data-widths", "").Replace("[", "").Replace("]", "").Split(',');
                        return dataSrc.Replace("{width}", dataWidths[dataWidths.Length - 1]);
                    default:
                        throw new InvalidDataException($"Invalid value type for GetHtmlValue: {type.Path}");
                }
            }

            throw new InvalidDataException($"Missing value type for GetHtmlValue: {configuration.Path}");
        }

        async Task<HtmlDocument> LoadItem(Uri uri)
        {
            // Responses are expected to be around 100 KB, so a 8s delay means about a maximum throughput of 100 Kbps.
            await Task.Delay(8000);
            var response = await Client.GetAsync(uri);
            var document = new HtmlDocument();
            document.Load(await response.Content.ReadAsStreamAsync());
            return document;
        }
    }
}
