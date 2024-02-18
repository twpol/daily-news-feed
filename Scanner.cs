using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        readonly bool Debug;

        public Scanner(IConfigurationSection configuration, Storage storage, HttpClient client, bool debug)
        {
            Configuration = configuration;
            Storage = storage;
            Client = client;
            Debug = debug;
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
            var blockNode = document.DocumentNode.SelectNodes(configuration["BlockSelector"]);
            if (blockNode == null)
            {
                Console.Error.WriteLine($"No nodes match BlockSelector {configuration["BlockSelector"]}");
                return;
            }

            var keyRegExp = new Regex(configuration["KeyRegExp"]);
            var stories = blockNode.SelectMany(node => node.SelectNodes(configuration["StorySelector"]));
            if (stories.Count() == 0)
            {
                Console.Error.WriteLine($"No nodes match StorySelector {configuration["StorySelector"]}");
                return;
            }

            var seenStories = new HashSet<string>();
            var storyIndex = 0;
            var tagCount = 0;
            foreach (var story in stories)
            {
                var keyMatch = keyRegExp.Match(GetHtmlValue(story, configuration.GetSection("KeySelector")));
                if (!keyMatch.Success)
                {
                    Console.Error.WriteLine($"No match for KeySelector on {story.OuterHtml}");
                    continue;
                }

                var key = keyMatch.Groups[1].Value;
                var imageUrl = new Uri(uri, GetHtmlValue(story, configuration.GetSection("ImageUrlSelector")));
                var title = GetHtmlValue(story, configuration.GetSection("TitleSelector"));
                var description = GetHtmlValue(story, configuration.GetSection("DescriptionSelector"));
                var url = new Uri(uri, GetHtmlValue(story, configuration.GetSection("UrlSelector")));

                if (seenStories.Contains(key))
                {
                    continue;
                }
                seenStories.Add(key);

                var insideDocument = await LoadItem(url);
                var insideImageUrl = new Uri(url, GetHtmlValue(insideDocument.DocumentNode, configuration.GetSection("InsideImageUrlSelector")));
                var insideTitle = GetHtmlValue(insideDocument.DocumentNode, configuration.GetSection("InsideTitleSelector"));
                var insideDescription = GetHtmlValue(insideDocument.DocumentNode, configuration.GetSection("InsideDescriptionSelector"));
                var insideLede = GetHtmlValue(insideDocument.DocumentNode, configuration.GetSection("InsideLedeSelector"));

                if (Debug)
                {
                    Console.WriteLine($"    - Key:           {key}");
                    Console.WriteLine($"      Image:         {imageUrl}");
                    Console.WriteLine($"      Title:         {title}");
                    Console.WriteLine($"      Description:   {description}");
                    Console.WriteLine($"      URL:           {url}");
                    Console.WriteLine($"        Image:       {insideImageUrl}");
                    Console.WriteLine($"        Title:       {insideTitle}");
                    Console.WriteLine($"        Description: {insideDescription}");
                    Console.WriteLine($"        Lede:        {insideLede}");
                }

                storyIndex++;
                await Storage.ExecuteNonQueryAsync("INSERT INTO Stories (Date, Site, Block, Position, Key, Url, ImageUrl, Title, Description) VALUES (@Param0, @Param1, @Param2, @Param3, @Param4, @Param5, @Param6, @Param7, @Param8)",
                    dateTime,
                    Configuration.Key,
                    configuration.Key,
                    storyIndex,
                    key,
                    url.ToString(),
                    GetFirstValue(insideImageUrl.ToString(), imageUrl.ToString()),
                    GetFirstValue(insideTitle, title),
                    GetFirstValue(insideLede, insideDescription, description)
                );

                var tags = insideDocument.DocumentNode.SelectNodes(configuration["InsideTagsSelector"] ?? "./no-match");
                if (tags != null)
                {
                    foreach (var tag in tags)
                    {
                        var tagUrl = new Uri(uri, tag.Attributes?["href"]?.Value);

                        tagCount++;
                        await Storage.ExecuteNonQueryAsync("INSERT INTO Tags (Url, Title) VALUES (@Param0, @Param1) ON CONFLICT DO NOTHING",
                            tagUrl.ToString(),
                            tag.InnerText
                        );
                        await Storage.ExecuteNonQueryAsync("INSERT INTO StoryTags (Date, Story, Tag) VALUES (@Param0, @Param1, @Param2) ON CONFLICT DO NOTHING",
                            dateTime,
                            url.ToString(),
                            tagUrl.ToString()
                        );
                    }
                }
            }

            Console.WriteLine($"    Collected {storyIndex} stories with {tagCount} tags");
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
                        return node.SelectSingleNode(type.Value)?.InnerHtml ?? $"<default:{type.Path}>";
                    case "InnerText":
                        return WhitespacePattern.Replace(WebUtility.HtmlDecode(node.SelectSingleNode(type.Value)?.InnerText ?? $"<default:{type.Path}>"), " ").Trim();
                    case "ResponsiveImage":
                        var img = node.SelectSingleNode(type.Value);
                        if (img == null)
                        {
                            return $"<default:{type.Path}>";
                        }
                        var dataSrc = img.GetAttributeValue("data-src", "");
                        var dataWidths = img.GetAttributeValue("data-widths", "").Replace("[", "").Replace("]", "").Split(',');
                        return dataSrc.Replace("{width}", dataWidths[dataWidths.Length - 1]);
                    default:
                        throw new InvalidDataException($"Invalid value type for GetHtmlValue: {type.Path}");
                }
            }

            throw new InvalidDataException($"Missing value type for GetHtmlValue: {configuration.Path}");
        }

        static string GetFirstValue(params string[] htmlValues)
        {
            foreach (var htmlValue in htmlValues)
            {
                if (htmlValue.Length > 0 && !htmlValue.Contains("<default:"))
                {
                    return htmlValue;
                }
            }
            return null;
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
