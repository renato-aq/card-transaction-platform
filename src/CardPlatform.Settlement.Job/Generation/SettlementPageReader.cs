using System.Runtime.CompilerServices;

using CardPlatform.Settlement.Job.ReadModel;

using Microsoft.EntityFrameworkCore;

namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// Reads the settlement entries of a run as a stream, in the order of the file, one page (query) at a time, with keyset
/// pagination and never <c>OFFSET</c>. Rows are projected, not tracked, and handed out as they arrive from the database.
/// </summary>
/// <remarks>
/// <para>
/// The reading is driven by the merchant accounts, in the order of their ids: the merchant ids are read a page at a time
/// (<c>id &gt; last id</c>), and the entries of each merchant a page at a time with
/// <see cref="SettlementQuery.MerchantEntries"/>: the entries after the key of the last entry read, compared as a row
/// value on the key within the merchant, <c>(transaction_id, id) &gt; (…)</c>. A single keyset query over
/// all merchants would leave the plan to PostgreSQL's estimate of how many entries belong to merchant accounts, which is
/// far too low (merchants are few accounts holding most of the credits), and it then sorts every remaining entry of the
/// day for each page.
/// </para>
/// <para>
/// The reader only reads: numbering and writing are left to the caller, and the entries of one merchant can be read on
/// their own (<see cref="ReadMerchantAsync"/>). The rows are streamed only if the <see cref="DbContext"/> does not retry
/// on failure: a retrying execution strategy makes EF Core buffer each whole result set before returning its first row.
/// </para>
/// </remarks>
public sealed class SettlementPageReader(LedgerReadDbContext dbContext)
{
    /// <summary>
    /// The settlement entries of <paramref name="options"/>, in the order of the file, read
    /// <see cref="RunOptions.PageSize"/> entries (or merchant ids) per query.
    /// </summary>
    public async IAsyncEnumerable<SettlementRow> ReadAsync(
        RunOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        await foreach (Guid merchant in MerchantsAsync(options, cancellationToken).ConfigureAwait(false))
        {
            await foreach (SettlementRow row in ReadMerchantAsync(options, merchant, cancellationToken).ConfigureAwait(false))
            {
                yield return row;
            }
        }
    }

    /// <summary>
    /// The ids of the merchant accounts, in the order of the file (by id, compared by the database), read
    /// <see cref="RunOptions.PageSize"/> ids per query. No query is open while an id is handed out, so the caller can
    /// query the same context in between.
    /// </summary>
    public async IAsyncEnumerable<Guid> MerchantsAsync(
        RunOptions options,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PageSize);

        Guid? last = null;
        List<Guid> page;
        do
        {
            page = await MerchantPage(options.PageSize, last).ToListAsync(cancellationToken).ConfigureAwait(false);
            foreach (Guid merchant in page)
            {
                yield return merchant;
            }

            last = page.Count > 0 ? page[^1] : last;
        }
        while (page.Count == options.PageSize);
    }

    /// <summary>
    /// The settlement entries of <paramref name="options"/> credited to <paramref name="merchantAccountId"/>, in the
    /// order of the file, read <see cref="RunOptions.PageSize"/> entries per query.
    /// </summary>
    public async IAsyncEnumerable<SettlementRow> ReadMerchantAsync(
        RunOptions options,
        Guid merchantAccountId,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.PageSize);

        SettlementKey? after = null;
        int rowsInPage;
        do
        {
            rowsInPage = 0;
            await foreach (SettlementRow row in EntryPage(options, merchantAccountId, after)
                .WithCancellation(cancellationToken)
                .ConfigureAwait(false))
            {
                rowsInPage++;
                after = row.Key;
                yield return row;
            }
        }
        while (rowsInPage == options.PageSize);
    }

    // One page of merchant ids: at most pageSize, the first ones after the given id.
    private IQueryable<Guid> MerchantPage(int pageSize, Guid? after)
    {
        IQueryable<AccountRecord> merchants = SettlementQuery.MerchantAccounts(dbContext).AsNoTracking();
        if (after is { } last)
        {
            merchants = merchants.Where(account => EF.Functions.GreaterThan(
                ValueTuple.Create(account.Id),
                ValueTuple.Create(last)));
        }

        return merchants
            .OrderBy(account => account.Id)
            .Take(pageSize)
            .Select(account => account.Id);
    }

    // One page of a merchant's entries: at most PageSize, the first ones after the given key.
    private IAsyncEnumerable<SettlementRow> EntryPage(RunOptions options, Guid merchantAccountId, SettlementKey? after)
    {
        return SettlementQuery.MerchantEntries(dbContext, options, merchantAccountId, after, options.PageSize)
            .AsAsyncEnumerable();
    }
}
