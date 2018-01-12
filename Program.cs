using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace DailyNewsFeed
{
    class Program
    {
        static void Main(string[] args)
        {
            MainAsync(args).Wait();
        }

        static async Task MainAsync(string[] args)
        {
            try
            {
                var configuration = LoadConfiguration(args);
                var storage = await LoadStorage(configuration);
                var client = CreateHttpClient();

                foreach (var section in configuration.GetSection("Sites").GetChildren())
                {
                    var scanner = new Scanner(section, storage, client);
                    await scanner.Process();
                }

                storage.Close();
            }
            catch (CommandLineParser.Exceptions.CommandLineException e)
            {
                Console.WriteLine(e.Message);
            }
        }

        static IConfigurationRoot LoadConfiguration(string[] args)
        {
            var config = new CommandLineParser.Arguments.FileArgument('c', "config")
            {
                DefaultValue = new FileInfo("config.json")
            };

            var commandLineParser = new CommandLineParser.CommandLineParser()
            {
                Arguments = {
                    config,
                }
            };

            commandLineParser.ParseCommandLine(args);

            return new ConfigurationBuilder()
                .AddJsonFile(config.Value.FullName, true)
                .Build();
        }

        static async Task<Storage> LoadStorage(IConfigurationRoot configuration)
        {
            var storage = new Storage(configuration.GetConnectionString("Storage"));
            await storage.Open();
            await storage.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS Stories (Date DATETIME, Site text, Block text, Position integer, Key text, Url text, ImageUrl text, Title text, Description text)");
            return storage;
        }

        static HttpClient CreateHttpClient()
        {
            var clientHandler = new HttpClientHandler()
            {
                CookieContainer = new CookieContainer(),
            };
            var client = new HttpClient(clientHandler);
            client.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("JGR-DailyNewsFeed", "1.0"));
            return client;
        }
    }
}
