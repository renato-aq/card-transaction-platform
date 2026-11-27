# 13. Generate the settlement file by streaming merchant partitions in parallel

**Status:** Accepted

## Context

The Settlement Job writes, once a day, the file that tells each merchant what it is owed: every credit entry to a
merchant account, in one currency, whose transaction was created within one UTC day. Its `run` command reads the
Ledger's tables (`accounts`, `transactions`, `entries`) in `ledgerdb` directly and only reads them (ADR 0004 and its
amendment of 2026-11-26; the `seed` command, a development tool, writes to them, as the end of this decision says). A
day of a card platform is millions of records, so building the whole file in memory costs memory that grows with the
day, and the reading must not slow down as it moves through the day.

The job started from a deliberately naive version, kept to be measured against: one EF Core query with `Include` and
change tracking loads every entry of the day and its transaction, builds the list of records, and only then writes the
file. Its memory grows with the number of records. This decision covers how the job reads and writes the day instead,
and the format of the file both versions write, which had to be fixed first so that the two could be compared byte for
byte.

## Decision

**The file.** One file per settlement date and currency, `settlement-<yyyyMMdd>-<currency>.txt`, fixed-width ASCII:
every record is 117 characters followed by `\n`, a header (`0`), one detail per entry (`1`: sequence number from 1,
transaction id, merchant account id, amount in minor units, instant in UTC) and a trailer (`9`: number of details and
sum of their amounts). `FileLayout/SettlementFileLayout.cs` is the single declaration of the positions.

- **Fixed width, not CSV**, because it is the usual shape of settlement files: positional, no quoting or escaping
  rules, every line the same length.
- **One file per currency**, because the trailer sums the amounts and amounts in different currencies cannot be added
  (nor share one number of minor units).
- **The content depends only on the data.** The formatter's output depends only on its inputs: no generation time,
  locale or random value is written. So two runs over the same data write the same bytes, which is what lets every
  strategy and every parallelism be checked against the others by hash. The file is written under a temporary name
  and moved to its final name only when complete; a failed run leaves no new or partial file (the file of an earlier
  successful run for the same date and currency stays as it was).

**The order is a contract, applied by PostgreSQL.** Details are ordered by merchant account id, then transaction id,
then entry id, as PostgreSQL compares `uuid` values (by their bytes). .NET's `Guid` compares differently, so the job
never sorts or compares ids in memory: every order, including the order of the merchants and of the partitions below,
comes from an `ORDER BY` in the database.

**Two strategies, selected by `--strategy`.** `naive` (version 1) is kept as the baseline, unchanged, so the
optimization can be measured and its file compared. `optimized` (version 2) is the default:

- **Untracked streaming.** Rows are untracked projections, handed out as they arrive (`IAsyncEnumerable`), and written
  as they are read, formatted into a reused buffer of lines written to the file in one call per batch. The trailer is
  accumulated in the same pass, so the data is read once and nothing of it is kept after its line is written.
- **Keyset pagination, driven per merchant, never `OFFSET`.** The merchant account ids are read a page at a time
  (`id > last id`); for each merchant, its credits of the day are read a page at a time (10,000 rows by default,
  `--page-size`) after the key of the last row read. The page is hand-written SQL (`SettlementQuery.MerchantEntries`):
  equality on `account_id` plus a row-value comparison `(transaction_id, id) > (…)`, which are the columns of the
  index below, so a page can start its index scan at the key and stop at the limit (whether it does depends on the plan
  PostgreSQL picks); each entry's transaction is looked up by primary key (`CROSS JOIN LATERAL … LIMIT 1`) to filter on
  the day.
- **One snapshot.** The whole read is one repeatable-read transaction, so every page sees the same ledger, as the single
  query of version 1 does.
- **No retrying execution strategy.** With a retrying execution strategy EF Core buffers each whole result set before
  returning its first row, which defeats streaming; that is the reason recorded in the code. The optimized path disables
  it (`DisableRetry`); `naive` keeps the retries it was measured with. A transient database failure ends the run with
  exit code 1 and no new or partial file (an earlier run's file for the same date and currency stays in place), and the
  run can simply be started again: it only reads, and it replaces the file of an earlier run.

**Why per merchant and hand-written SQL.** A single keyset query over all the merchants' entries would be simpler. The
reason recorded when the reader was written (`SettlementPageReader`) for not using one is that such a query leaves the
plan to PostgreSQL's estimate of how many entries belong to merchant accounts, which is far too low (merchants are few
accounts holding most of the credits), and the plan then sorts every remaining entry of the day for each page. The page
is SQL rather than LINQ because EF Core does not produce that shape: it turns the per-entry lookup of the transaction
into a join over every transaction (`SettlementQuery`). Neither observation is part of the published benchmark; what
the benchmark does measure is the cost of the chosen shape, below.

**Index.** The Ledger migration `20261126185917_AddEntriesAccountTransactionIdIndex` replaces `ix_entries_account_id
(account_id)` with `ix_entries_account_id_transaction_id_id (account_id, transaction_id, id)`, the key of a page within
a merchant. The table keeps the same number of indexes (the new one also serves lookups by account), with a wider key.
The migration is an ordinary `CREATE INDEX`, not `CONCURRENTLY`, so it blocks writes to `entries` while it builds.

**Parallel partitions.** With `--parallelism` n above 1 (the default is 4):

1. One repeatable-read transaction exports its snapshot (`pg_export_snapshot()`) and then reads the merchant ids, in
   the database's order.
2. The list is split into at most n **contiguous ranges** of merchants (`SettlementPartitions`), sizes differing by at
   most one; every entry of a merchant falls in one partition, and the partitions concatenated in order list every
   merchant once, in the file's order. No id is compared in .NET: the ranges are positions in the list the database
   sorted.
3. The first partition is read by the exporting transaction; each other one on a connection of its own, in a
   repeatable-read transaction that imports the snapshot (`SET TRANSACTION SNAPSHOT`) before reading anything. Every
   partition therefore sees exactly the same rows, and the run holds at most n connections.
4. Each partition writes its details to an intermediate file next to the settlement file, formatted as in the final
   file but **numbered from 1 within the partition**, and returns its own trailer.
5. **The global sequence number is applied at the join.** The partitions are appended to the settlement file in the
   order of their ranges, each as soon as it and every partition before it are complete; appending copies the lines as
   they were formatted and rewrites only the sequence field, continuing after the details already in the file, and adds
   the partition's count and total to the file's single trailer. The intermediate file is deleted after its append.
6. The first partition to fail cancels the others, and the run fails with its error. Intermediate files are deleted on
   success and on failure (only killing the process can leave them, as it can the temporary settlement file).

Splitting by contiguous ranges and renumbering at the join is what keeps the file identical to the sequential one: the
order of the details within a partition is the database's, the order of the partitions is the database's order of the
merchants, and the sequence numbers are assigned once the count of everything before a partition is known.

**Alternatives considered and rejected:**

- **Full load in memory** (version 1). Simple, one query, and the database does one sort; but its memory grows with
  the number of records, since every record is loaded and tracked before the first line is written: a peak working set
  of 5,919.8 MiB for 5,000,000 records (below). Kept only as the baseline.
- **`OFFSET` pagination.** Each page makes PostgreSQL produce and discard every row before the offset, so a page costs
  more the further into the day it is, and the whole read grows quadratically with the number of pages. Without one
  snapshot, rows inserted or removed between pages also shift the offsets and skip or repeat rows. It was not
  implemented or measured here; the keyset form costs the same at any position and does not depend on positions.
- **A single global keyset query** over all merchants: see "Why per merchant" above.
- **Partitioning by hash or modulo of the merchant id.** Balances the merchants without reading the list first, but
  every partition then holds merchants from all over the order, so the partitions must be merged by comparing ids, and
  that comparison would be .NET's `Guid` order, not PostgreSQL's, or another round trip to the database.
- **Each partition counts its records first to learn its first sequence number**, so it can write final numbers
  directly into place. The count would repeat most of the work of the read (the same index scan and transaction
  lookups), for every partition but the first; rewriting a fixed-width field at the join costs only a pass over bytes
  already on local disk.
- **CSV**: rejected for the reason given above; the content would be the same.
- **One file with all currencies**: its trailer would have to sum amounts of different currencies, or carry one total
  per currency, which the usual layout does not have.
- **Splitting the work across processes or machines.** Out of scope: one process with a few connections is enough for
  the volumes measured, and the database, not the job, is what parallelism exercises.

## Consequences

The numbers below come from [the Settlement Job benchmark](../benchmarks.md), the job and PostgreSQL on the same
laptop. The table is its main result: one seeded day of 5,000,000 records over 1,000 merchants, median of 5 runs; the
figures measured on other data are given with their setup where they appear. They hold for that machine and that data
shape; the document lists their limits.

| Configuration | Elapsed | Peak working set | CPU time (job) | Allocated |
|---|---|---|---|---|
| `naive` | 38.808 s | 5,919.8 MiB | 36.827 s | 14.66 GiB |
| `optimized`, parallelism 1 | 48.915 s | 148.9 MiB | 9.959 s | 1.75 GiB |
| `optimized`, parallelism 4 (default) | 21.998 s | 152.1 MiB | 10.837 s | 1.76 GiB |
| `optimized`, parallelism 8 | 17.966 s | 152.4 MiB | 10.932 s | 1.77 GiB |

- **Memory no longer depends on the day.** The optimized strategy peaked at about 149–152 MiB at any parallelism,
  about 39 times less than `naive`, and allocated about 8 times less. All 24 files of the main runs had the same
  SHA-256, and their trailers matched an independent SQL count and total.
- **The optimized strategy is not faster by itself.** At parallelism 1 it was 26% slower than `naive` (48.9 s against
  38.8 s): it issues one query per merchant and looks up each entry's transaction by primary key, and the job waits for
  PostgreSQL most of the time (9.959 s of CPU in 48.915 s). The time gain comes only from parallelism: 1.76 times
  faster than `naive` at 4 partitions, 2.16 times at 8.
- **Cost grows with the merchants' history, not only with the day.** Every credit of every merchant account is read
  and its transaction looked up before the date filters it out. With a second day of 5,000,000 records, and its own
  1,000 merchants, seeded into the same database (2,000 merchants in all; medians of 3 runs), settling the first day
  was 47% to 89% slower with the optimized strategy and 14% slower with `naive`; parallelism 4 was then about as fast as
  `naive` (41.7 s against 44.4 s). A ledger that keeps months of history would
  make the optimized strategy slower still; this decision does not address it.
- **Partitions balance merchants, not records.** A day concentrated on a few merchants gains little from parallelism:
  in an earlier measurement taken during development, with one merchant holding 450,000 of 500,000 records,
  parallelism 8 took 2.1 s against 2.3 s at parallelism 1.
- **The index matters only when a merchant spans several pages.** With the benchmark's data (about 5,000 entries per
  merchant, fewer than a page of 10,000) it made no measurable difference (medians of 3 runs, parallelism 1 and 4). On
  two databases of 500,000 records, at parallelism 1 with pages of 1,000 (medians of 3 runs): with 10 merchants of about
  50,000 records each it halved the time (4.021 s to 1.891 s); with a single merchant holding all 500,000 records the
  old index was not slow (1.731 s against 1.502 s). Its key is wider: in the 5,000,000-record database it takes 739 MB
  as filled row by row by the seed, 574 MB when built in one statement, against 70 MB for the old `(account_id)` index.
- **Disk.** A parallel run needs, for a while, up to about one more copy of the details on disk next to the file, in
  the intermediate files (the benchmark's file is 590,000,236 bytes).
- **Connections.** Each partition holds one PostgreSQL connection for the whole run (8 at parallelism 8), out of the
  server's limit (100 by default) shared with the services; the default of 4 is kept low for that reason. The gain from
  4 to 8 partitions was 1.22 times; higher degrees were not measured.
- **No retry on the optimized path.** A transient database error fails the run (exit code 1, no new file) and it has
  to be started again; `naive` keeps EF Core's retries.
- **The job is coupled to the Ledger's table schema**, not to its projects: its own read model (`LedgerReadDbContext`,
  table and column names, stored values) and the hand-written SQL of the page. A change to the Ledger's tables can
  break the job without a compile error. The job's tests create the schema by applying the Ledger's migrations, which is
  what catches such a change; the Ledger's migrations must keep the index this decision relies on.
- **`seed` writes to the Ledger's tables.** To have millions of rows to measure, the job's `seed` command bulk-loads
  generated purchases with `COPY`, bypassing the Ledger API but keeping its invariants. It is a development and
  benchmark tool, not part of the daily run, which only reads.
