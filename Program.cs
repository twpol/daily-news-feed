using System;
using System.IO;
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
            return storage;
        }
    }
}
