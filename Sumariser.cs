using System;
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
            var endDTO = DateTimeOffset.UtcNow;
            var startDTO = endDTO.AddSeconds(-uint.Parse(Configuration["SummaryTimePeriodS"]));
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
