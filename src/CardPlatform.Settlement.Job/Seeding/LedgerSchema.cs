using Npgsql;

namespace CardPlatform.Settlement.Job.Seeding;

/// <summary>
/// The ledger tables the seed writes to. The schema is owned by the Ledger (its EF Core migrations in
/// <c>CardPlatform.Ledger.Infrastructure</c>); this job only couples to the table and column names and to the values
/// stored in them (enums as their names, amounts as <c>numeric(18,2)</c>, currencies as <c>char(3)</c>).
/// </summary>
public static class LedgerSchema
{
    public const string Accounts = "accounts";

    public const string Transactions = "transactions";

    public const string Entries = "entries";

    // Account types and entry directions, stored by the ledger as their enum names.
    public const string CardAccount = "Card";

    public const string MerchantAccount = "Merchant";

    public const string SettlementAccount = "Settlement";

    public const string Debit = "Debit";

    public const string Credit = "Credit";

    private static readonly string[] RequiredTables = [Accounts, Transactions, Entries];

    /// <summary>Returns the ledger tables that do not exist in the database, in the current schema.</summary>
    public static async Task<IReadOnlyList<string>> FindMissingTablesAsync(
        NpgsqlDataSource dataSource,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(dataSource);

        NpgsqlCommand command = dataSource.CreateCommand(
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = current_schema() AND table_name = ANY($1)
            """);
        await using (command.ConfigureAwait(false))
        {
            command.Parameters.Add(new NpgsqlParameter { Value = RequiredTables });

            HashSet<string> existing = new(StringComparer.Ordinal);
            NpgsqlDataReader reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
            await using (reader.ConfigureAwait(false))
            {
                while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
                {
                    existing.Add(reader.GetString(0));
                }
            }

            return [.. RequiredTables.Where(table => !existing.Contains(table))];
        }
    }
}
