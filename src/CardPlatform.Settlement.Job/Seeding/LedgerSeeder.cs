using System.Diagnostics;

using Npgsql;

using NpgsqlTypes;

namespace CardPlatform.Settlement.Job.Seeding;

/// <summary>
/// Loads generated purchases straight into the ledger tables with binary <c>COPY</c>, bypassing the Ledger API, so the
/// settlement job has millions of rows to measure against. Everything is written in one database transaction: a failed
/// or cancelled seed leaves nothing behind.
/// </summary>
/// <remarks>
/// <para>
/// The seed creates its own accounts and never touches existing ones: one <c>Settlement</c> account, the merchants and
/// the cards. Each purchase debits a card and credits a merchant, at an instant within the requested UTC day. As a card
/// can never be negative (ADR 0005), each card that buys anything is first funded by a top-up from the settlement
/// account (debit settlement, credit card) dated the day before the purchases, of its purchases' total rounded up to a
/// multiple of <see cref="TopUpStep"/>. So top-ups are never on the requested day and never credit a merchant.
/// </para>
/// <para>
/// The invariants of the ledger hold: every transaction has one debit and one credit of the same amount, and the stored
/// balance of every created account is the net of its entries (credits − debits). Balances are computed by a first,
/// in-memory pass over the generator, which returns the same purchases every time, so accounts are written once with
/// their final balance. Transaction ids are UUID v7 derived from the transaction instant, as the ledger does.
/// </para>
/// </remarks>
public sealed partial class LedgerSeeder(NpgsqlDataSource dataSource, ILogger<LedgerSeeder> logger)
{
    /// <summary>Description of the generated purchase transactions.</summary>
    public const string PurchaseDescription = "Seeded purchase";

    /// <summary>Description of the generated card top-ups.</summary>
    public const string TopUpDescription = "Seeded card top-up";

    /// <summary>Top-ups are the card's purchases total rounded up to a multiple of this amount, in major units.</summary>
    public const int TopUpStep = 100;

    /// <summary>Purchases written per <c>COPY</c>; progress is reported after each batch.</summary>
    public const int BatchSize = 100_000;

    private static readonly TimeSpan TopUpTimeOfDay = TimeSpan.FromHours(12);

    private static readonly TimeSpan CopyTimeout = TimeSpan.FromMinutes(10);

    /// <summary>Generates the purchases described by <paramref name="options"/> and writes them to the ledger.</summary>
    public async Task<SeedResult> SeedAsync(SeedOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        long startedAt = Stopwatch.GetTimestamp();
        LogStarting(options.Transactions, options.Merchants, options.Cards, options.Date, options.Currency, options.Seed);

        PurchaseGenerator generator = new(options);
        SeedPlan plan = SeedPlan.Create(generator, options);

        NpgsqlConnection connection = await dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
        await using (connection.ConfigureAwait(false))
        {
            NpgsqlTransaction transaction = await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            await using (transaction.ConfigureAwait(false))
            {
                await WriteAccountsAsync(connection, plan, options.Currency, generator, cancellationToken).ConfigureAwait(false);
                long topUps = await WriteTopUpsAsync(connection, plan, options.Currency, generator, cancellationToken)
                    .ConfigureAwait(false);
                await WritePurchasesAsync(connection, plan, options, generator, cancellationToken).ConfigureAwait(false);

                await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);

                SeedResult result = new(options.Transactions, topUps, plan.AccountCount, Stopwatch.GetElapsedTime(startedAt));
                LogFinished(result.Purchases, result.TopUps, result.Accounts, result.Elapsed);
                return result;
            }
        }
    }

    private static async Task WriteAccountsAsync(
        NpgsqlConnection connection,
        SeedPlan plan,
        string currency,
        PurchaseGenerator generator,
        CancellationToken cancellationToken)
    {
        NpgsqlBinaryImporter importer = await connection.BeginBinaryImportAsync(
            $"COPY {LedgerSchema.Accounts} (id, type, currency, created_at, balance) FROM STDIN (FORMAT BINARY)",
            cancellationToken).ConfigureAwait(false);
        await using (importer.ConfigureAwait(false))
        {
            importer.Timeout = CopyTimeout;

            await WriteAccountAsync(
                importer,
                plan.SettlementAccount,
                LedgerSchema.SettlementAccount,
                currency,
                generator.ToAmount(-plan.TotalTopUpUnits),
                cancellationToken).ConfigureAwait(false);

            for (int merchant = 0; merchant < plan.MerchantAccounts.Length; merchant++)
            {
                await WriteAccountAsync(
                    importer,
                    plan.MerchantAccounts[merchant],
                    LedgerSchema.MerchantAccount,
                    currency,
                    generator.ToAmount(plan.MerchantUnits[merchant]),
                    cancellationToken).ConfigureAwait(false);
            }

            for (int card = 0; card < plan.CardAccounts.Length; card++)
            {
                await WriteAccountAsync(
                    importer,
                    plan.CardAccounts[card],
                    LedgerSchema.CardAccount,
                    currency,
                    generator.ToAmount(plan.TopUpUnits[card] - plan.CardUnits[card]),
                    cancellationToken).ConfigureAwait(false);
            }

            await importer.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    private static async Task WriteAccountAsync(
        NpgsqlBinaryImporter importer,
        PlannedAccount account,
        string type,
        string currency,
        decimal balance,
        CancellationToken cancellationToken)
    {
        await importer.StartRowAsync(cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(account.Id, NpgsqlDbType.Uuid, cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(type, NpgsqlDbType.Varchar, cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(currency, NpgsqlDbType.Char, cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(account.CreatedAt.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(balance, NpgsqlDbType.Numeric, cancellationToken).ConfigureAwait(false);
    }

    private static async Task<long> WriteTopUpsAsync(
        NpgsqlConnection connection,
        SeedPlan plan,
        string currency,
        PurchaseGenerator generator,
        CancellationToken cancellationToken)
    {
        DateTimeOffset firstTopUpAt = generator.DayStart.AddDays(-1).Add(TopUpTimeOfDay);
        List<(int Card, Guid TransactionId, DateTimeOffset CreatedAt)> topUps = [];
        for (int card = 0; card < plan.CardAccounts.Length; card++)
        {
            if (plan.TopUpUnits[card] > 0)
            {
                DateTimeOffset createdAt = firstTopUpAt.AddTicks(card * TimeSpan.TicksPerMicrosecond);
                topUps.Add((card, Guid.CreateVersion7(createdAt), createdAt));
            }
        }

        NpgsqlBinaryImporter transactions = await BeginTransactionsImportAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        await using (transactions.ConfigureAwait(false))
        {
            foreach ((int _, Guid transactionId, DateTimeOffset createdAt) in topUps)
            {
                await WriteTransactionAsync(transactions, transactionId, TopUpDescription, createdAt, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transactions.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        NpgsqlBinaryImporter entries = await BeginEntriesImportAsync(connection, cancellationToken).ConfigureAwait(false);
        await using (entries.ConfigureAwait(false))
        {
            foreach ((int card, Guid transactionId, DateTimeOffset _) in topUps)
            {
                decimal amount = generator.ToAmount(plan.TopUpUnits[card]);
                await WriteEntryAsync(entries, transactionId, plan.SettlementAccount.Id, LedgerSchema.Debit, amount, currency, cancellationToken)
                    .ConfigureAwait(false);
                await WriteEntryAsync(entries, transactionId, plan.CardAccounts[card].Id, LedgerSchema.Credit, amount, currency, cancellationToken)
                    .ConfigureAwait(false);
            }

            await entries.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        return topUps.Count;
    }

    private async Task WritePurchasesAsync(
        NpgsqlConnection connection,
        SeedPlan plan,
        SeedOptions options,
        PurchaseGenerator generator,
        CancellationToken cancellationToken)
    {
        List<(SeededPurchase Purchase, Guid TransactionId)> batch = new(Math.Min(BatchSize, options.Transactions));
        long written = 0;

        foreach (SeededPurchase purchase in generator.Generate())
        {
            batch.Add((purchase, Guid.CreateVersion7(purchase.CreatedAt)));
            if (batch.Count == BatchSize)
            {
                written += await WritePurchaseBatchAsync(connection, plan, options.Currency, generator, batch, cancellationToken)
                    .ConfigureAwait(false);
                LogProgress(written, options.Transactions);
            }
        }

        if (batch.Count > 0)
        {
            written += await WritePurchaseBatchAsync(connection, plan, options.Currency, generator, batch, cancellationToken)
                .ConfigureAwait(false);
            LogProgress(written, options.Transactions);
        }
    }

    private static async Task<int> WritePurchaseBatchAsync(
        NpgsqlConnection connection,
        SeedPlan plan,
        string currency,
        PurchaseGenerator generator,
        List<(SeededPurchase Purchase, Guid TransactionId)> batch,
        CancellationToken cancellationToken)
    {
        NpgsqlBinaryImporter transactions = await BeginTransactionsImportAsync(connection, cancellationToken)
            .ConfigureAwait(false);
        await using (transactions.ConfigureAwait(false))
        {
            foreach ((SeededPurchase purchase, Guid transactionId) in batch)
            {
                await WriteTransactionAsync(transactions, transactionId, PurchaseDescription, purchase.CreatedAt, cancellationToken)
                    .ConfigureAwait(false);
            }

            await transactions.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        NpgsqlBinaryImporter entries = await BeginEntriesImportAsync(connection, cancellationToken).ConfigureAwait(false);
        await using (entries.ConfigureAwait(false))
        {
            foreach ((SeededPurchase purchase, Guid transactionId) in batch)
            {
                decimal amount = generator.ToAmount(purchase.Units);
                await WriteEntryAsync(entries, transactionId, plan.CardAccounts[purchase.Card].Id, LedgerSchema.Debit, amount, currency, cancellationToken)
                    .ConfigureAwait(false);
                await WriteEntryAsync(entries, transactionId, plan.MerchantAccounts[purchase.Merchant].Id, LedgerSchema.Credit, amount, currency, cancellationToken)
                    .ConfigureAwait(false);
            }

            await entries.CompleteAsync(cancellationToken).ConfigureAwait(false);
        }

        int count = batch.Count;
        batch.Clear();
        return count;
    }

    private static async Task<NpgsqlBinaryImporter> BeginTransactionsImportAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        NpgsqlBinaryImporter importer = await connection.BeginBinaryImportAsync(
            $"COPY {LedgerSchema.Transactions} (id, description, created_at) FROM STDIN (FORMAT BINARY)",
            cancellationToken).ConfigureAwait(false);
        importer.Timeout = CopyTimeout;
        return importer;
    }

    private static async Task<NpgsqlBinaryImporter> BeginEntriesImportAsync(
        NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        // The id is an identity column generated by the database.
        NpgsqlBinaryImporter importer = await connection.BeginBinaryImportAsync(
            $"COPY {LedgerSchema.Entries} (transaction_id, account_id, direction, amount, currency) FROM STDIN (FORMAT BINARY)",
            cancellationToken).ConfigureAwait(false);
        importer.Timeout = CopyTimeout;
        return importer;
    }

    private static async Task WriteTransactionAsync(
        NpgsqlBinaryImporter importer,
        Guid id,
        string description,
        DateTimeOffset createdAt,
        CancellationToken cancellationToken)
    {
        await importer.StartRowAsync(cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(id, NpgsqlDbType.Uuid, cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(description, NpgsqlDbType.Text, cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(createdAt.UtcDateTime, NpgsqlDbType.TimestampTz, cancellationToken).ConfigureAwait(false);
    }

    private static async Task WriteEntryAsync(
        NpgsqlBinaryImporter importer,
        Guid transactionId,
        Guid accountId,
        string direction,
        decimal amount,
        string currency,
        CancellationToken cancellationToken)
    {
        await importer.StartRowAsync(cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(transactionId, NpgsqlDbType.Uuid, cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(accountId, NpgsqlDbType.Uuid, cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(direction, NpgsqlDbType.Varchar, cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(amount, NpgsqlDbType.Numeric, cancellationToken).ConfigureAwait(false);
        await importer.WriteAsync(currency, NpgsqlDbType.Char, cancellationToken).ConfigureAwait(false);
    }

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Seeding {Transactions} purchases over {Merchants} merchants and {Cards} cards on {Date} in {Currency} (seed {Seed})")]
    private partial void LogStarting(int transactions, int merchants, int cards, DateOnly date, string currency, int seed);

    [LoggerMessage(Level = LogLevel.Information, Message = "Seeded {Written} of {Total} purchases")]
    private partial void LogProgress(long written, long total);

    [LoggerMessage(
        Level = LogLevel.Information,
        Message = "Seed finished: wrote {Purchases} purchase transactions, {TopUps} card top-ups and {Accounts} accounts in {Elapsed}")]
    private partial void LogFinished(long purchases, long topUps, long accounts, TimeSpan elapsed);
}
