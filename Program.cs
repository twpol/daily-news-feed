﻿using System;
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
                ParseCommandLine(args, out var config, out var debug, out var fetch, out var summary);
                var configuration = LoadConfiguration(config);
                var storage = await LoadStorage(configuration, debug.Value);
                var client = CreateHttpClient();

                if (fetch.Value)
                {
                    foreach (var section in configuration.GetSection("Sites").GetChildren())
                    {
                        var scanner = new Scanner(section, storage, client, debug.Value);
                        await scanner.Process();
                    }
                }

                if (summary.Value)
                {
                    foreach (var section in configuration.GetSection("Sites").GetChildren())
                    {
                        var sumariser = new Sumariser(section, storage);
                        await sumariser.Process();
                    }
                }

                storage.Close();
            }
            catch (CommandLineParser.Exceptions.CommandLineException e)
            {
                Console.WriteLine(e.Message);
            }
        }

        static void ParseCommandLine(string[] args, out CommandLineParser.Arguments.FileArgument config, out CommandLineParser.Arguments.SwitchArgument debug, out CommandLineParser.Arguments.SwitchArgument fetch, out CommandLineParser.Arguments.SwitchArgument summary)
        {
            config = new CommandLineParser.Arguments.FileArgument('c', "config")
            {
                ForcedDefaultValue = new FileInfo("config.json")
            };

            debug = new CommandLineParser.Arguments.SwitchArgument('d', "debug", false);

            fetch = new CommandLineParser.Arguments.SwitchArgument('f', "fetch", false);

            summary = new CommandLineParser.Arguments.SwitchArgument('s', "summary", false);

            var commandLineParser = new CommandLineParser.CommandLineParser()
            {
                Arguments = {
                    config,
                    debug,
                    fetch,
                    summary,
                }
            };

            commandLineParser.ParseCommandLine(args);
        }

        static IConfigurationRoot LoadConfiguration(CommandLineParser.Arguments.FileArgument config)
        {
            return new ConfigurationBuilder()
                .AddJsonFile(config.Value.FullName, true)
                .Build();
        }

        static async Task<Storage> LoadStorage(IConfigurationRoot configuration, bool debug)
        {
            var storage = new Storage(configuration.GetConnectionString("Storage"), debug);
            await storage.Open();
            await storage.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS Stories (Date datetime, Site text, Block text, Position integer, Key text, Url text, ImageUrl text, Title text, Description text)");
            await storage.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS StoryTags (Date datetime, Story text, Tag text)");
            await storage.ExecuteNonQueryAsync("CREATE TABLE IF NOT EXISTS Tags (Url text, Title text, UNIQUE (Url))");
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
