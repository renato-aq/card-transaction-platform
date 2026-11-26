using Npgsql;

namespace CardPlatform.Settlement.Job.Tests.Infrastructure;

/// <summary>Plain SQL reads of a ledger database, for the assertions of the seed tests.</summary>
internal static class LedgerDatabase
{
    public static async Task<T> ScalarAsync<T>(string connectionString, string sql, params object[] parameters)
    {
        NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using (dataSource.ConfigureAwait(false))
        {
            NpgsqlCommand command = dataSource.CreateCommand(sql);
            await using (command.ConfigureAwait(false))
            {
                foreach (object parameter in parameters)
                {
                    command.Parameters.Add(new NpgsqlParameter { Value = parameter });
                }

                object? value = await command.ExecuteScalarAsync().ConfigureAwait(false);
                return (T)value!;
            }
        }
    }

    public static async Task<List<T>> ListAsync<T>(
        string connectionString,
        string sql,
        Func<NpgsqlDataReader, T> read,
        params object[] parameters)
    {
        NpgsqlDataSource dataSource = NpgsqlDataSource.Create(connectionString);
        await using (dataSource.ConfigureAwait(false))
        {
            NpgsqlCommand command = dataSource.CreateCommand(sql);
            await using (command.ConfigureAwait(false))
            {
                foreach (object parameter in parameters)
                {
                    command.Parameters.Add(new NpgsqlParameter { Value = parameter });
                }

                List<T> rows = [];
                NpgsqlDataReader reader = await command.ExecuteReaderAsync().ConfigureAwait(false);
                await using (reader.ConfigureAwait(false))
                {
                    while (await reader.ReadAsync().ConfigureAwait(false))
                    {
                        rows.Add(read(reader));
                    }
                }

                return rows;
            }
        }
    }
}
