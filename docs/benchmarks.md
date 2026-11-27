# Settlement Job benchmark

This document compares the two strategies of the [Settlement Job](../README.md#settlement-job) on one seeded day of
**5,000,000 settlement records**: `naive` (version 1, the baseline) and `optimized` (version 2) with a parallelism of
1, 4 (the default) and 8. The result tables come from the JSON written by `--metrics-output` (the run summary of the
job), converted to seconds, MiB or GiB. The seed times, the table and index sizes and the `EXPLAIN ANALYZE` figure come
from the seed's log and from SQL. A few figures are labelled as earlier measurements: they were taken during
development, at 500,000 records on the same machine, and are not part of this benchmark's recorded runs.

In short, on this machine and this data:

- **Memory:** the optimized strategy peaked at about 149–152 MiB whatever the parallelism, against 5,919.8 MiB for
  `naive` (median), about 39 times less, and it allocated about 8 times less.
- **Time:** with a parallelism of 1 the optimized strategy was **slower** than `naive` (48.9 s against 38.8 s, +26%).
  It was faster only in parallel: 22.0 s with 4 partitions (1.76 times faster than `naive`) and 18.0 s with 8 (2.16
  times faster).
- **CPU:** the job process used 10–11 s of CPU with the optimized strategy at any parallelism, against 36.8 s with
  `naive`; at parallelism 1 that is about a fifth of the elapsed time (9.96 s in 48.9 s), the rest spent waiting for
  the database, and about half and three fifths of it at 4 and 8 (10.8 s in 22.0 s, 10.9 s in 18.0 s).
- All 24 files of the main runs have the same SHA-256, and their trailer matches the SQL count and total.
- The index added for the optimized strategy made no measurable difference on this data (about 5,000 entries per
  merchant, fewer than a page); on a smaller set with 50,000 records per merchant and pages of 1,000 it halved the time.
- The optimized strategy's time grows with all the merchants' credits in the database, not only the settled day's: with
  a second day of 5,000,000 records in the same database it was 47% to 89% slower, `naive` 14%.

## Environment

| | |
|---|---|
| Machine | MacBook Air (Mac16,12), Apple M4, 10 cores (4 performance, 6 efficiency), 16 GB RAM, internal SSD |
| Operating system | macOS 27.0.1 |
| .NET | SDK 10.0.403, runtime Microsoft.NETCore.App 10.0.14, Release build |
| Container runtime | OrbStack 2.2.5 (Docker Engine 29.8.2); its Linux VM is limited to 10 CPUs and 8,192 MiB of memory, and the container has no CPU or memory limit of its own |
| PostgreSQL | 18.6 (image `postgres:18.6`, Debian, aarch64), default configuration: `shared_buffers` 128 MB, `work_mem` 4 MB, `max_connections` 100 |
| Placement | the job runs on the host (macOS), PostgreSQL in the container on the same machine, connected through `127.0.0.1` |

The database is a disposable container used only for the benchmark, with the Ledger schema applied from the
repository's migrations; it is not the Aspire database of the platform.

## Data

One run of `seed` with its defaults except the date: `seed --transactions 5000000 --date 2026-11-20` (1,000
merchants, 100,000 cards, BRL, generator seed 42). The seed took **9 min 31 s** (571.67 s for the whole process; the
final `ANALYZE` took 2.8 s of it).

| What | Count |
|---|---|
| Purchase transactions on 2026-11-20 (one card debit and one merchant credit each) | 5,000,000 |
| Card top-ups on 2026-11-19 (settlement debit, card credit) | 100,000 |
| Transactions / entries / accounts | 5,100,000 / 10,200,000 / 101,001 |
| Settlement records of 2026-11-20 (SQL below) | 5,000,000, totalling 1,252,094,439.09 BRL |
| Settlement file | 590,000,236 bytes (5,000,002 lines of 118 bytes) |

The purchases are spread uniformly over the 1,000 merchants (about 5,000 each) and over the cards, at uniformly random
instants of the day, with amounts from 1.00 to 500.00.

| Table | Data | Indexes | Total |
|---|---|---|---|
| `entries` | 905 MB | 1,270 MB (`ix_entries_account_id_transaction_id_id` 739 MB, `ix_entries_transaction_id` 312 MB, `pk_entries` 219 MB) | 2,175 MB |
| `transactions` | 333 MB | 193 MB | 526 MB |
| `accounts` | 7,896 kB | 4,176 kB | 12 MB |

(Sizes as reported by PostgreSQL's `pg_size_pretty`, right after the seed.)

## Methodology

- **What is measured.** Each run is a new process of the job (`run` command) that writes the whole file of
  2026-11-20. The job measures only the generation itself, from just before the first query to the completed file:
  opening the database connections, reading, formatting, writing, and for a parallel run joining the partitions. Host
  start-up, the seed and the process exit are not measured.
- **The metrics** are the job's run summary, written as JSON by `--metrics-output`:
  - elapsed time: wall-clock time (`Stopwatch`);
  - CPU time: user and kernel time of the whole job process, all threads (`Process.TotalProcessorTime`); it does not
    include the CPU used by PostgreSQL;
  - peak working set: the highest working set of the job process, sampled every 50 ms by a background thread, plus one
    sample at the start and one at the end (`Process.WorkingSet64`; macOS does not report a peak of its own). A spike
    shorter than 50 ms can be missed;
  - allocated bytes: bytes allocated by the .NET GC during the run, all threads.
  
  None of them measures the database server.
- **Runs.** Four configurations: `naive`, `optimized` with `--parallelism 1`, 4 and 8, all with the default page size
  (10,000). Six rounds, each running the four configurations one after the other in that order, so they are
  interleaved; round 0 is a warm-up and is discarded (its numbers are listed below), rounds 1–5 are the measured
  runs. The database and the operating system caches were not cleared between runs: the published runs are warm.
  Nothing else heavy ran on the machine during the runs (no build, no tests); the platform was not running.
- **Median.** Each metric's median over the five measured runs is published on its own, so the medians of one row
  may come from different runs.
- **Statistics.** `seed` ends with `ANALYZE accounts, transactions, entries`, so the planner has statistics for the new
  rows from the first run. Without it, a run right after a bulk load depends on when autovacuum gets to the tables: in
  an earlier measurement taken during development (500,000 records, one merchant), the optimized strategy took 14.7 s
  before autoanalyze ran and 1.7 s after.
- **Correctness.** The SHA-256 of every file is compared, and the trailer of every file (record count and total) is
  compared with the SQL below.

```sql
SELECT count(*), sum(e.amount)
FROM entries e
JOIN accounts a ON a.id = e.account_id
JOIN transactions t ON t.id = e.transaction_id
WHERE a.type = 'Merchant' AND e.direction = 'Credit' AND e.currency = 'BRL'
  AND t.created_at >= '2026-11-20T00:00:00Z' AND t.created_at < '2026-11-21T00:00:00Z';
```

## Results

Median of 5 runs, 5,000,000 records:

| Configuration | Elapsed | Peak working set | CPU time | Allocated | Elapsed vs `naive` | Peak memory vs `naive` |
|---|---|---|---|---|---|---|
| `naive` | 38.808 s | 5,919.8 MiB | 36.827 s | 14.66 GiB | — | — |
| `optimized`, parallelism 1 | 48.915 s | 148.9 MiB | 9.959 s | 1.75 GiB | 1.26× (26% slower) | 39.8× less |
| `optimized`, parallelism 4 (default) | 21.998 s | 152.1 MiB | 10.837 s | 1.76 GiB | 0.57× (1.76× faster) | 38.9× less |
| `optimized`, parallelism 8 | 17.966 s | 152.4 MiB | 10.932 s | 1.77 GiB | 0.46× (2.16× faster) | 38.9× less |

What the numbers show:

- The memory of the optimized strategy does not depend on the number of records: 148.9 MiB here, against the
  146–149 MiB of an earlier measurement taken during development on 500,000 records. Parallelism adds about 3 MiB. The memory
  of `naive` grows with the records: every entry and its transaction are tracked EF Core entities held until the file
  is written.
- The optimized strategy is not faster by itself. With one connection it issues one query per merchant and looks up
  each entry's transaction by primary key (see [Limitations](#limitations)), and the job process uses CPU for about a
  fifth of the run, waiting for PostgreSQL the rest: 9.959 s of CPU in 48.915 s. `naive` reads the day in one query and
  keeps its CPU busy (36.827 s of CPU in 38.808 s). In the earlier development measurement on 500,000 records the two
  took about the same time (about 3.9–4.1 s); at 5,000,000 the sequential optimized run is the slower one.
- Parallel partitions are what makes it faster: 2.22 times faster at 4 than at 1, 1.22 times faster again at 8.
  The CPU time of the job barely changes with the parallelism, so the gain comes from the database answering several
  queries at the same time. Each partition holds one PostgreSQL connection for the whole run. The job's CPU time is
  then about half of the elapsed time at 4 (10.837 s in 21.998 s) and three fifths at 8 (10.932 s in 17.966 s).
- `naive`'s peak varied between runs, from 4,865.4 MiB to 6,058.8 MiB (3,861.0 MiB in the warm-up); why it varies
  was not measured, and on this machine it may understate what `naive` touched (see the memory pressure limitation in
  [Limitations](#limitations)). The optimized strategy varied by less than 1 MiB.

### Individual runs

Elapsed and CPU time in milliseconds, peak working set and allocated bytes in bytes, exactly as in the JSON files. Run
0 is the discarded warm-up.

`naive`:

| Run | Elapsed (ms) | CPU (ms) | Peak working set (bytes) | Allocated (bytes) |
|---|---|---|---|---|
| 0 (warm-up) | 32441.055 | 32265.815 | 4048535552 | 15746207728 |
| 1 | 39584.454 | 37779.012 | 5101699072 | 15348527456 |
| 2 | 38825.449 | 36938.303 | 6125273088 | 15739143112 |
| 3 | 37644.008 | 35700.347 | 6207324160 | 15738200144 |
| 4 | 38807.668 | 36826.604 | 6353076224 | 15355013568 |
| 5 | 38201.3 | 36311.579 | 6289276928 | 15740505704 |

`optimized`, parallelism 1:

| Run | Elapsed (ms) | CPU (ms) | Peak working set (bytes) | Allocated (bytes) |
|---|---|---|---|---|
| 0 (warm-up) | 56647.933 | 9898.885 | 156286976 | 1881998224 |
| 1 | 48915.399 | 9981.548 | 155926528 | 1881727216 |
| 2 | 49046.496 | 9810.8 | 156008448 | 1880341832 |
| 3 | 49009.681 | 10044.065 | 156155904 | 1879773280 |
| 4 | 48899.292 | 9901.914 | 156106752 | 1880623136 |
| 5 | 48569.087 | 9959.175 | 156106752 | 1879100832 |

`optimized`, parallelism 4:

| Run | Elapsed (ms) | CPU (ms) | Peak working set (bytes) | Allocated (bytes) |
|---|---|---|---|---|
| 0 (warm-up) | 26873.46 | 11573.809 | 158433280 | 1889496024 |
| 1 | 21998.136 | 10956.041 | 158875648 | 1892051440 |
| 2 | 22085.075 | 11182.622 | 159531008 | 1893850808 |
| 3 | 22069.831 | 10541.754 | 159580160 | 1890991880 |
| 4 | 21938.231 | 10837.047 | 159301632 | 1894925688 |
| 5 | 21938.467 | 10438.348 | 159727616 | 1885280608 |

`optimized`, parallelism 8:

| Run | Elapsed (ms) | CPU (ms) | Peak working set (bytes) | Allocated (bytes) |
|---|---|---|---|---|
| 0 (warm-up) | 19709.735 | 10886.665 | 159744000 | 1897553424 |
| 1 | 17859.429 | 10676.829 | 159956992 | 1901370696 |
| 2 | 18400.745 | 11517.828 | 159760384 | 1903497192 |
| 3 | 18184.935 | 11036.377 | 159744000 | 1903240504 |
| 4 | 17862.156 | 10932.147 | 159481856 | 1901414920 |
| 5 | 17966.437 | 10517.612 | 160514048 | 1900668976 |

### Same file

Every one of the 24 files (warm-up included) has the SHA-256
`0d4bd4ed95384f6b2896a915ae0265eadea816dde95c8e2c6e237f44f998f6b0` and the trailer
`9005000000000000125209443909`: 5,000,000 details totalling 125,209,443,909 cents, the count and the total of the SQL
query above. The hash is specific to this database: ids are UUID v7, whose random part changes with every seed, so a
reproduction gets a different hash, the same for every configuration.

## Index effect

The optimized strategy reads each merchant's entries with the index `ix_entries_account_id_transaction_id_id
(account_id, transaction_id, id)`, added by the Ledger migration `20261126185917_AddEntriesAccountTransactionIdIndex`,
which replaced `ix_entries_account_id (account_id)`. To see what the new index changes, the 5,000,000-record database
was put back to the old shape, measured, and then restored to the migration's state:

```sql
DROP INDEX ix_entries_account_id_transaction_id_id;
CREATE INDEX ix_entries_account_id ON entries (account_id);
ANALYZE entries;
-- measure, then restore:
DROP INDEX ix_entries_account_id;
CREATE INDEX ix_entries_account_id_transaction_id_id ON entries (account_id, transaction_id, id);
ANALYZE entries;
```

Both indexes were built in one statement on the loaded table, so both are compact (old index 70 MB; composite index
574 MB, against 739 MB when it was filled row by row by the seed). Each shape: one warm-up and three runs per
configuration, interleaved, median published.

| Index | Configuration | Elapsed | Peak working set | CPU time |
|---|---|---|---|---|
| old: `(account_id)` | `optimized`, parallelism 1 | 48.391 s | 148.9 MiB | 10.031 s |
| new: `(account_id, transaction_id, id)` | `optimized`, parallelism 1 | 47.971 s | 148.8 MiB | 9.877 s |
| old: `(account_id)` | `optimized`, parallelism 4 | 21.061 s | 151.7 MiB | 10.725 s |
| new: `(account_id, transaction_id, id)` | `optimized`, parallelism 4 | 21.608 s | 152.3 MiB | 10.997 s |

Individual runs (elapsed, ms): old index, parallelism 1: 49384.888, 48390.654, 47197.6 (warm-up 41795.692);
parallelism 4: 22742.76, 20874.471, 21061.471 (warm-up 20331.842). New index, parallelism 1: 47376.752, 48228.099,
47971.058 (warm-up 42503.3); parallelism 4: 21924.236, 21265.617, 21608.278 (warm-up 19382.135). All 16 files have the
same SHA-256 as the main runs.

**With this data the new index makes no measurable difference**: the differences are within the spread of the runs.
Each merchant has about 5,000 entries, fewer than a page (10,000), so every merchant is read in a single query,
without the keyset condition `(transaction_id, id) > (…)` that the composite index is there to serve.

The index matters when a merchant has more entries than a page. To see that, two smaller databases were seeded in the
same container, each with 500,000 records on 2026-11-20 and the job run with `--page-size 1000`, parallelism 1, one
warm-up and three runs per index shape, median published. Here the index swap was not the one above: the new index
was measured first, as the seed left it (filled row by row), and then it was dropped and the old one built on the loaded
table and analyzed, and measured; at 5,000,000 records both shapes were built in one statement on the loaded table.

| Data | Index | Elapsed | CPU time | Peak working set |
|---|---|---|---|---|
| 10 merchants, about 50,000 records each (`seed --transactions 500000 --merchants 10`) | old: `(account_id)` | 4.021 s | 2.350 s | 148.9 MiB |
| | new: `(account_id, transaction_id, id)` | 1.891 s | 1.524 s | 143.7 MiB |
| 1 merchant with all 500,000 records (`seed --transactions 500000 --merchants 1`) | old: `(account_id)` | 1.731 s | 1.548 s | 143.4 MiB |
| | new: `(account_id, transaction_id, id)` | 1.502 s | 1.603 s | 143.7 MiB |

Individual runs (elapsed, ms): 10 merchants, old index 4023.858, 4013.718, 4020.512 (warm-up 4081.126), new index
1891.155, 1880.304, 2017.518 (warm-up 2341.828); 1 merchant, old index 1633.849, 1768.47, 1731.355 (warm-up
1776.836), new index 1703.194, 1501.664, 1453.436 (warm-up 2024.751). Within each database, the files of both shapes
had the same SHA-256.

With 10 merchants the new index halved the time (4.021 s to 1.891 s): with only `(account_id)` the database cannot
start a page at the key inside the merchant's entries in index order, so it reads more of them for every page of 1,000
(the plan of each page was not inspected in this case). With a single merchant holding every record the old shape was
not slow: PostgreSQL planned each page on `ix_entries_transaction_id`, reading the entries in transaction id order
from the key and filtering by account, which is cheap when the merchant owns half of all the entries (checked with
`EXPLAIN ANALYZE` on one page: about 2.5 ms). An earlier measurement taken during development on another
single-merchant set of 500,000 records (78.8 s with the
old index against about 1.5 s with the new one, page size 1,000) could not be reproduced here. So the new index matters
when a merchant has many more entries than a page, and how much depends on the plan PostgreSQL picks; with the default
seed it changes nothing.

## History effect

The optimized strategy reads every merchant account and, for each one, all its credit entries, looking up each
entry's transaction before filtering on the date; so its cost follows all the credits of the merchants in the
database, not only the settled day's. The main database holds a single seeded day. To measure what more data costs,
a second day of 5,000,000 purchases was seeded into the same database after the index runs, so with the rebuilt
composite index (`seed --transactions 5000000 --date 2026-11-21`, 9 min 45 s), and the four configurations were run
again on 2026-11-20: one warm-up and three runs each, interleaved, median published.

The second seed creates its own 1,000 merchants, so the database then had 2,000 merchants, 10,000,000 merchant credits,
10,200,000 transactions and 20,400,000 entries. The day 2026-11-20 still had 5,000,000 records (same SQL count and
total), and every file still had the SHA-256 and trailer above.

| Configuration | Elapsed, one day | Elapsed, two days | Change | CPU time, two days | Peak working set, two days |
|---|---|---|---|---|---|
| `naive` | 38.808 s | 44.404 s | +14% | 36.571 s | 5,047.9 MiB |
| `optimized`, parallelism 1 | 48.915 s | 72.016 s | +47% | 23.524 s | 150.1 MiB |
| `optimized`, parallelism 4 | 21.998 s | 41.685 s | +89% | 17.847 s | 150.9 MiB |
| `optimized`, parallelism 8 | 17.966 s | 33.895 s | +89% | 21.108 s | 152.9 MiB |

(One day: the medians of the main results, five runs; two days: medians of three runs.)

Individual runs (elapsed, ms; CPU, ms): `naive` 44638.813 / 36816.124, 44403.979 / 36571.187, 43724.246 / 36130.297
(warm-up 53175.4 / 43663.519); parallelism 1: 69686.596 / 24377.477, 72015.677 / 23524.266, 72381.247 / 23506.056
(warm-up 76987.406 / 24901.343); parallelism 4: 41685.391 / 17711.316, 41969.24 / 17932.83, 40832.832 / 17847.163
(warm-up 39964.797 / 17518.626); parallelism 8: 33996.306 / 21107.676, 33894.764 / 21083.358, 33484.523 / 21204.262
(warm-up 33485.209 / 20240.043). Peak working sets: `naive` 4597809152, 5511839744, 5293064192 bytes; the optimized
runs between 156925952 and 161546240 bytes.

One more day of data, on merchants with no record on the settled day, made `naive` 14% slower and the optimized
strategy 47% to 89% slower. With two days, parallelism 4 was about as fast as `naive` (41.7 s against 44.4 s) and
parallelism 8 was 1.31 times faster, against 2.16 times with one day. A ledger that keeps months of history would make
the optimized strategy slower still; its memory did not change. The job's own CPU time at parallelism 1 also more than
doubled for the same file, from 9.959 s to 23.524 s; the code accounts for one more query per added merchant (2,000
instead of 1,000, the new ones finding no record of the day), while the other day's entries are looked up and
discarded inside PostgreSQL, so where the rest of the job's extra CPU goes was not measured. In an earlier measurement
taken during development (a second day of 1,500,000 records next to a day of 500,000), the sequential optimized run
went from 4.0 s to 8–9 s while `naive` stayed at 4.5 s.

## Limitations

- **One machine.** The job and PostgreSQL share the same laptop (10 cores, 16 GB) and compete for its CPU, memory and
  disk; PostgreSQL runs in a Linux VM with its default configuration. A dedicated database server, a tuned
  configuration or network latency between the job and the database would change the times, and probably the
  ratios. The laptop has no fan, so a long series of runs could be throttled; the configurations were interleaved so
  that any such drift affects them alike.
- **Memory pressure.** The peak working set is the job process's resident memory as sampled by the job. On this
  16 GB machine, `naive`'s working set of 5–6 GiB coexists with the container runtime's VM of up to 8 GiB. In a
  separate run at 5,000,000 records (not one of the published runs), the macOS memory compressor grew by about
  1.9–2.3 GiB and swap grew during the `naive` runs. So `naive`'s peak working set may understate the memory it
  touched, and its elapsed time may include the cost of compression. This does not change the direction of any result.
- **A single seeded day.** The main database holds one day of purchases. The optimized strategy issues one query
  per merchant account and filters the date after looking up each entry's transaction, so its cost grows with every
  credit the merchants have, on every day, not only with the day being settled; see [History effect](#history-effect).
- **Uniform merchants.** The seed spreads the purchases uniformly over 1,000 merchants. The partitions of a parallel
  run hold the same number of merchants, not of records, so with a skewed distribution the parallel gain shrinks: in
  an earlier measurement taken during development, with one merchant holding 450,000 of 500,000 records, parallelism 8 took 2.1 s against
  2.3 s at parallelism 1.
- **Parallelism costs connections.** Each partition holds a PostgreSQL connection for the whole run (8 at parallelism
  8), out of the server's limit (100 by default) shared with the services. The gain from 4 to 8 partitions was 1.22
  times; higher degrees were not measured.
- **No retry in the optimized strategy.** It streams rows, which EF Core's retrying execution strategy would prevent,
  so it runs without retries: a transient database error fails the run (exit code 1, no file), and the run has to be
  started again. `naive` keeps EF Core's retries.
- **Warm caches.** The published runs are warm (one discarded warm-up per configuration, run right after the seed).
  The warm-ups differ from the measured runs in both directions: 56.6 s against a median of 48.9 s for the sequential
  optimized run, 32.4 s against 38.8 s for `naive`, so a single cold run says little.
- **What is not measured:** the database server's own CPU and memory, host start-up, and the seed.

## How to reproduce

Prerequisites: the .NET 10 SDK, Docker (or OrbStack), `jq`, and about 20 GB of free disk (the database and the 24
settlement files of 590 MB each; delete the files as you go if needed). The commands work in bash and zsh, from the
root of a clone of the repository; everything they write goes to `settlement-files/` (ignored by git) or to the
container.

1. Start a disposable PostgreSQL 18.6, reachable only from this machine, with trust authentication (no password):

   ```bash
   docker run -d --name settlement-benchmark -p 127.0.0.1:55432:5432 \
     -e POSTGRES_HOST_AUTH_METHOD=trust -e POSTGRES_DB=ledgerdb postgres:18.6
   until docker exec settlement-benchmark pg_isready -h 127.0.0.1 -U postgres -q; do sleep 1; done
   ```

2. Create the Ledger schema from the repository's migrations:

   ```bash
   dotnet tool restore
   dotnet restore src/CardPlatform.Ledger.Infrastructure
   mkdir -p settlement-files/benchmark
   dotnet ef migrations script --idempotent --project src/CardPlatform.Ledger.Infrastructure \
     --output settlement-files/benchmark/ledger-schema.sql
   docker exec -i settlement-benchmark psql -U postgres -d ledgerdb -v ON_ERROR_STOP=1 -q \
     < settlement-files/benchmark/ledger-schema.sql
   ```

3. Build the job in Release, point it at the database and seed the day (about 10 minutes for 5,000,000; for a quick
   check of the whole procedure, use `--transactions 200000` here and keep everything else the same):

   ```bash
   dotnet build src/CardPlatform.Settlement.Job -c Release
   export ConnectionStrings__ledgerdb='Host=127.0.0.1;Port=55432;Database=ledgerdb;Username=postgres'
   job() { dotnet src/CardPlatform.Settlement.Job/bin/Release/net10.0/CardPlatform.Settlement.Job.dll "$@"; }
   time (job seed --transactions 5000000 --date 2026-11-20)
   ```

   (The parentheses make `time` report in zsh too, whose `time` ignores shell functions; the seed also logs its own
   elapsed time, in its `Seed finished: …` line.)

4. Count the records the file must have:

   ```bash
   docker exec settlement-benchmark psql -U postgres -d ledgerdb -c "
     SELECT count(*), sum(e.amount)
     FROM entries e
     JOIN accounts a ON a.id = e.account_id
     JOIN transactions t ON t.id = e.transaction_id
     WHERE a.type = 'Merchant' AND e.direction = 'Credit' AND e.currency = 'BRL'
       AND t.created_at >= '2026-11-20T00:00:00Z' AND t.created_at < '2026-11-21T00:00:00Z'"
   ```

5. Run the four configurations, interleaved, six rounds (round 0 is the warm-up), each run in a new process:

   ```bash
   measure() { # name, round, strategy, parallelism
     job run --date 2026-11-20 --strategy "$3" --parallelism "$4" \
       --output "settlement-files/benchmark/$1-$2" --metrics-output "settlement-files/benchmark/metrics/$1-$2.json"
   }
   for round in 0 1 2 3 4 5; do
     measure naive "$round" naive 1
     measure optimized-p1 "$round" optimized 1
     measure optimized-p4 "$round" optimized 4
     measure optimized-p8 "$round" optimized 8
   done
   ```

   This loop keeps all 24 files for step 6; the published runs instead recorded the SHA-256 and the trailer of each
   file and deleted it right after its run, so only one 590 MB file existed at a time.

6. Check that every file is the same and that the trailers match step 4 (`9`, the count in 9 digits, the total in
   cents in 18 digits):

   ```bash
   shasum -a 256 settlement-files/benchmark/*/settlement-20261120-BRL.txt
   tail -q -n 1 settlement-files/benchmark/*/settlement-20261120-BRL.txt | cut -c1-28 | sort | uniq -c
   ```

7. Compute the medians of rounds 1–5:

   ```bash
   for config in naive optimized-p1 optimized-p4 optimized-p8; do
     jq -s -c --arg config "$config" 'def median: sort | .[length / 2 | floor];
       {config: $config, runs: length,
        elapsedMilliseconds: (map(.elapsedMilliseconds) | median),
        cpuTimeMilliseconds: (map(.cpuTimeMilliseconds) | median),
        peakWorkingSetBytes: (map(.peakWorkingSetBytes) | median),
        allocatedBytes: (map(.allocatedBytes) | median)}' settlement-files/benchmark/metrics/"$config"-[1-5].json
   done
   ```

8. For the [index effect](#index-effect), run the SQL of that section with
   `docker exec -i settlement-benchmark psql -U postgres -d ledgerdb`, measuring `optimized-p1` and `optimized-p4`
   (as in step 5, with other names) between the two halves. The smaller sets go to a database of their own:

   ```bash
   docker exec settlement-benchmark psql -U postgres -c 'CREATE DATABASE skewdb'
   docker exec -i settlement-benchmark psql -U postgres -d skewdb -v ON_ERROR_STOP=1 -q \
     < settlement-files/benchmark/ledger-schema.sql
   ConnectionStrings__ledgerdb='Host=127.0.0.1;Port=55432;Database=skewdb;Username=postgres' \
     job seed --transactions 500000 --merchants 10 --date 2026-11-20
   ConnectionStrings__ledgerdb='Host=127.0.0.1;Port=55432;Database=skewdb;Username=postgres' \
     job run --date 2026-11-20 --strategy optimized --parallelism 1 --page-size 1000 \
     --output settlement-files/benchmark/skew --metrics-output settlement-files/benchmark/skew.json
   ```

   For the [history effect](#history-effect), seed a second day in `ledgerdb` with
   `job seed --transactions 5000000 --date 2026-11-21` and repeat steps 5 to 7.

9. Remove the container and the files:

   ```bash
   docker rm -f -v settlement-benchmark
   rm -rf settlement-files/benchmark
   ```
