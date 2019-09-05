using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace DailyNewsFeed
{
	public class Sumariser
    {
        readonly IConfigurationSection Configuration;
        readonly Storage Storage;

        public Sumariser(IConfigurationSection configuration, Storage storage)
        {
            Configuration = configuration;
            Storage = storage;
        }

        public async Task Process()
        {
            var summaryConfig = Configuration.GetSection("Summary");
            var endDTO = DateTimeOffset.UtcNow;
            var startDTO = endDTO.AddSeconds(-uint.Parse(summaryConfig["TimePeriodS"]));

            if (summaryConfig["OutputHtmlFile"] != null)
            {
                var outfileHtmlFile = new FileInfo(summaryConfig["OutputHtmlFile"]);
                var maximumScore = float.Parse(summaryConfig["MaximumScore"] ?? "-1");
                using (var writer = outfileHtmlFile.CreateText())
                {
                    writer.WriteLine("<!doctype html>");
                    writer.WriteLine("<html>");
                    writer.WriteLine("<head>");
                    writer.WriteLine("    <meta charset=\"utf-8\">");
                    writer.WriteLine("    <meta http-equiv=\"x-ua-compatible\" content=\"ie=edge\">");
                    writer.WriteLine($"    <title>{endDTO:yyyy-MM-dd} {Configuration.Key} Daily News Feed</title>");
                    writer.WriteLine("    <meta name=\"viewport\" content=\"width=device-width, initial-scale=1, shrink-to-fit=no\">");
                    writer.WriteLine("    <link rel=\"stylesheet\" href=\"https://stackpath.bootstrapcdn.com/bootstrap/4.1.3/css/bootstrap.min.css\" integrity=\"sha384-MCw98/SFnGE8fJT3GXwEOngsV7Zt27NXFoaoApmYm81iuXoPkFOJwJ8ERdknLPMO\" crossorigin=\"anonymous\">");
                    writer.WriteLine("    <style>");
                    writer.WriteLine("        .media > img { max-height: 5rem; }");
                    writer.WriteLine("    </style>");
                    writer.WriteLine("</head>");
                    writer.WriteLine("<body>");
                    writer.WriteLine("    <main class=\"container\">");
                    writer.WriteLine($"        <h3>{endDTO:yyyy-MM-dd} {Configuration.Key} Daily News Feed</h3>");

                    writer.WriteLine("        <ul class=\"list-unstyled\">");
                    foreach (var block in Configuration.GetSection("Blocks").GetChildren())
                    {
                        writer.WriteLine("            <li class=\"\">");
                        writer.WriteLine($"                <h4>{block.Key}</h4>");
                        writer.WriteLine("                    <ol class=\"list-unstyled\">");

                        var reader = await Storage.ExecuteReaderAsync("SELECT Key, SUM(Position)*1.0/COUNT()/COUNT() As Position, SUM(Position), COUNT(), Url, ImageUrl, Title, Description FROM Stories WHERE Site = @Param0 AND Block = @Param1 AND @Param2 < Date AND Date < @Param3 GROUP BY Key ORDER BY Position ASC", Configuration.Key, block.Key, startDTO, endDTO);
                        while (await reader.ReadAsync())
                        {
                            if (maximumScore >= 0 && reader.GetDouble(1) > maximumScore) continue;
                            writer.WriteLine($"                        <li class=\"media\">");
                            writer.WriteLine($"                            <!-- score={reader.GetDouble(1)}, sum={reader.GetInt32(2)}, count={reader.GetInt32(3)} -->");
                            writer.WriteLine($"                            <div class=\"media-body\">");
                            writer.WriteLine($"                                <h5 class=\"my-1\"><a href=\"{reader.GetString(4)}\">{reader.GetString(6)}</a></h5>");
                            writer.WriteLine($"                                <p class=\"my-1\">");
                            var tagReader = await Storage.ExecuteReaderAsync("SELECT DISTINCT Tags.Url, Tags.Title FROM Tags JOIN StoryTags WHERE Tags.Url = StoryTags.Tag AND StoryTags.Story = @Param0", reader.GetString(4));
                            while (await tagReader.ReadAsync())
                            {
                                writer.WriteLine($"                                    <span class=\"badge badge-dark\">{tagReader.GetString(1)}</span>");
                            }
                            writer.WriteLine($"                                </p>");
                            writer.WriteLine($"                                <p class=\"my-1\">{reader.GetString(7)}</p>");
                            writer.WriteLine($"                            </div>");
                            writer.WriteLine($"                            <img class=\"ml-3\" src=\"{reader.GetString(5)}\">");
                            writer.WriteLine($"                        </li>");
                        }
                        writer.WriteLine("                    </ol>");
                        writer.WriteLine("            </li>");
                    }
                    writer.WriteLine("        </ul>");

                    writer.WriteLine($"        <p>{Configuration.Key} from {startDTO:u} until {endDTO:u}</p>");
                    writer.WriteLine("    </main>");
                    writer.WriteLine("</body>");
                    writer.WriteLine("</html>");
                }
                Console.WriteLine($"Written summary to <{outfileHtmlFile}>");
            }
            else
            {
                Console.WriteLine($"{Configuration.Key} from {startDTO} until {endDTO}:");

                foreach (var block in Configuration.GetSection("Blocks").GetChildren())
                {
                    Console.WriteLine($"  {block.Key}:");

                    var reader = await Storage.ExecuteReaderAsync("SELECT Key, SUM(Position)*1.0/COUNT()/COUNT() As Position, SUM(Position), COUNT(), Url, ImageUrl, Title, Description FROM Stories WHERE Site = @Param0 AND Block = @Param1 AND @Param2 < Date AND Date < @Param3 GROUP BY Key ORDER BY Position ASC", Configuration.Key, block.Key, startDTO, endDTO);
                    while (await reader.ReadAsync())
                    {
                        Console.WriteLine($"    {reader.GetString(4)} {reader.GetString(6)} ({reader.GetDouble(1)}, {reader.GetInt32(2)}/{reader.GetInt32(3)})");
                    }
                }
            }
        }
    }
}
