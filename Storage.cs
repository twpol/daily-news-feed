using System.Data.Common;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;

namespace DailyNewsFeed
{
    public class Storage
    {
        DbConnection Connection;

        public Storage(string connectionString)
        {
            Connection = new SqliteConnection();
            Connection.ConnectionString = connectionString;
        }

        public async Task Open()
        {
            await Connection.OpenAsync();
        }

        public void Close()
        {
            Connection.Close();
        }

        public async Task ExecuteNonQueryAsync(string commandText, params object[] parameters)
        {
            await CreateCommand(commandText, parameters).ExecuteNonQueryAsync();
        }

        public async Task<object> ExecuteScalarAsync(string commandText, params object[] parameters)
        {
            return await CreateCommand(commandText, parameters).ExecuteScalarAsync();
        }

        public async Task<DbDataReader> ExecuteReaderAsync(string commandText, params object[] parameters)
        {
            return await CreateCommand(commandText, parameters).ExecuteReaderAsync();
        }

        private DbCommand CreateCommand(string commandText, object[] parameters)
        {
            var command = Connection.CreateCommand();

            command.CommandText = commandText;

            for (var i = 0; i < parameters.Length; i++)
            {
                var param = command.CreateParameter();
                param.ParameterName = $"@Param{i}";
                param.Value = parameters[i];
                command.Parameters.Add(param);
            }

            return command;
        }
    }
}
