using System.Data.Common;
using System.Text.RegularExpressions;

using CardPlatform.Settlement.Job.ReadModel;

using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// Shares one snapshot of the ledger between the connections of a parallel run, with PostgreSQL's snapshot
/// synchronization: the transaction that reads the merchants exports its snapshot (<c>pg_export_snapshot()</c>) and every
/// other partition's transaction imports it (<c>SET TRANSACTION SNAPSHOT</c>) before reading anything, so all of them see
/// exactly the same rows, as the single repeatable read transaction of the sequential version does.
/// </summary>
/// <remarks>
/// Both statements run in the current transaction of the context, which must be a repeatable read transaction that has
/// not read anything yet when it imports. An exported snapshot can be imported only while the exporting transaction is
/// open, so that transaction must stay open until every import is done. They are sent on the context's connection as
/// plain commands: they control the transaction and read no ledger table.
/// </remarks>
public static partial class SettlementSnapshot
{
    /// <summary>Exports the snapshot of the current transaction of <paramref name="dbContext"/> and returns its id.</summary>
    public static async Task<string> ExportAsync(LedgerReadDbContext dbContext, CancellationToken cancellationToken)
    {
        DbCommand command = CreateCommand(dbContext, "SELECT pg_export_snapshot()");
        await using (command.ConfigureAwait(false))
        {
            object? snapshotId = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);
            return snapshotId is string id && IsSnapshotId(id)
                ? id
                : throw new InvalidOperationException("PostgreSQL did not return a snapshot id.");
        }
    }

    /// <summary>
    /// Makes the current transaction of <paramref name="dbContext"/>, which has not read anything yet, see the snapshot
    /// <paramref name="snapshotId"/> exported by <see cref="ExportAsync"/>.
    /// </summary>
    public static async Task ImportAsync(LedgerReadDbContext dbContext, string snapshotId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(snapshotId);

        // SET TRANSACTION SNAPSHOT takes no parameter: the id, a value returned by PostgreSQL, is checked to be one before
        // it is written into the statement.
        if (!IsSnapshotId(snapshotId))
        {
            throw new ArgumentException($"'{snapshotId}' is not a PostgreSQL snapshot id.", nameof(snapshotId));
        }

        DbCommand command = CreateCommand(dbContext, $"SET TRANSACTION SNAPSHOT '{snapshotId}'");
        await using (command.ConfigureAwait(false))
        {
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    /// <summary>Whether <paramref name="value"/> has the form of a PostgreSQL snapshot id, e.g. <c>00000003-0000001B-1</c>.</summary>
    public static bool IsSnapshotId(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return SnapshotIdPattern().IsMatch(value);
    }

    private static DbCommand CreateCommand(LedgerReadDbContext dbContext, string sql)
    {
        ArgumentNullException.ThrowIfNull(dbContext);

        IDbContextTransaction transaction = dbContext.Database.CurrentTransaction
            ?? throw new InvalidOperationException("A snapshot is exported or imported only inside a transaction.");
        DbCommand command = dbContext.Database.GetDbConnection().CreateCommand();
        command.Transaction = transaction.GetDbTransaction();
        command.CommandText = sql;
        return command;
    }

    [GeneratedRegex(@"\A[0-9A-F]+(-[0-9A-F]+)+\z", RegexOptions.CultureInvariant)]
    private static partial Regex SnapshotIdPattern();
}
