using CardPlatform.Settlement.Job.FileLayout;
using CardPlatform.Settlement.Job.Generation;

namespace CardPlatform.Settlement.Job.Tests.Generation;

public sealed class SettlementFileWriterTests : IDisposable
{
    private static readonly SettlementHeader Header = new(new DateOnly(2026, 11, 20), "BRL");

    private readonly string _outputDirectory = Path.Combine(Path.GetTempPath(), $"settlement-writer-tests-{Guid.NewGuid():N}");

    public void Dispose()
    {
        if (Directory.Exists(_outputDirectory))
        {
            Directory.Delete(_outputDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task Writing_in_batches_writes_the_same_file_as_writing_line_by_line()
    {
        // More than two batch buffers, in batches that end inside a buffer, an empty batch and lines in between.
        SettlementRecord[] records = [.. Enumerable.Range(1, (2 * SettlementFileWriter.BatchLines) + 77).Select(Record)];

        SettlementFileResult lineByLine = await WriteAsync("lines", async writer =>
        {
            foreach (SettlementRecord record in records)
            {
                await writer.WriteAsync(record, CancellationToken.None);
            }
        });
        SettlementFileResult batches = await WriteAsync("batches", async writer =>
        {
            await writer.WriteBatchAsync(records[..700], CancellationToken.None);
            await writer.WriteBatchAsync([], CancellationToken.None);
            await writer.WriteAsync(records[700], CancellationToken.None);
            await writer.WriteBatchAsync(records[701..], CancellationToken.None);
        });

        Assert.Equal(records.Length, batches.Trailer.DetailCount);
        Assert.Equal(lineByLine.Trailer, batches.Trailer);
        Assert.Equal(await File.ReadAllBytesAsync(lineByLine.Path), await File.ReadAllBytesAsync(batches.Path));
    }

    [Fact]
    public async Task A_batch_that_cannot_be_formatted_leaves_no_file_behind()
    {
        string directory = Path.Combine(_outputDirectory, "failed");
        SettlementFileWriter writer = await SettlementFileWriter.CreateAsync(directory, Header, CancellationToken.None);
        await using (writer)
        {
            // 10^13 is 10^15 cents: more digits than the amount field holds.
            SettlementRecord tooLarge = Record(3) with { Amount = 10_000_000_000_000m };

            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(
                () => writer.WriteBatchAsync([Record(1), Record(2), tooLarge], CancellationToken.None));
            Assert.Equal(0, writer.Trailer.DetailCount);
        }

        Assert.Empty(Directory.GetFiles(directory));
    }

    [Fact]
    public async Task Appending_partitions_writes_the_same_file_as_writing_their_records_in_order()
    {
        // Partitions of more than a batch buffer, of exactly one, empty and of a single record, numbered from 1 each.
        SettlementRecord[] records = [.. Enumerable.Range(1, (3 * SettlementFileWriter.BatchLines) + 3).Select(Record)];
        int[] partitionSizes = [SettlementFileWriter.BatchLines + 2, SettlementFileWriter.BatchLines, 0, 1, SettlementFileWriter.BatchLines];
        Assert.Equal(records.Length, partitionSizes.Sum());
        string partitionDirectory = Path.Combine(_outputDirectory, "partitions");
        Directory.CreateDirectory(partitionDirectory);
        List<(string Path, SettlementTrailer Trailer)> partitions = [];
        int start = 0;
        foreach (int size in partitionSizes)
        {
            string path = Path.Combine(partitionDirectory, $"part{partitions.Count}.tmp");
            SettlementPartitionWriter partition = SettlementPartitionWriter.Create(path, Header);
            await using (partition)
            {
                await partition.WriteBatchAsync(records[start..(start + size)], CancellationToken.None);
                await partition.CompleteAsync(CancellationToken.None);
                Assert.Equal(size, partition.Trailer.DetailCount);
                partitions.Add((path, partition.Trailer));
            }

            start += size;
        }

        SettlementFileResult direct = await WriteAsync("direct", writer => writer.WriteBatchAsync(records, CancellationToken.None));
        SettlementFileResult appended = await WriteAsync("appended", async writer =>
        {
            foreach ((string path, SettlementTrailer trailer) in partitions)
            {
                await writer.AppendPartitionAsync(path, trailer, CancellationToken.None);
            }
        });

        Assert.Equal(direct.Trailer, appended.Trailer);
        Assert.Equal(await File.ReadAllBytesAsync(direct.Path), await File.ReadAllBytesAsync(appended.Path));
    }

    [Fact]
    public async Task A_partition_whose_file_does_not_hold_the_details_of_its_trailer_is_rejected()
    {
        string partitionPath = Path.Combine(_outputDirectory, "partition.tmp");
        Directory.CreateDirectory(_outputDirectory);
        SettlementPartitionWriter partition = SettlementPartitionWriter.Create(partitionPath, Header);
        await using (partition)
        {
            await partition.WriteBatchAsync([Record(1), Record(2)], CancellationToken.None);
            await partition.CompleteAsync(CancellationToken.None);
        }

        string directory = Path.Combine(_outputDirectory, "failed");
        SettlementFileWriter writer = await SettlementFileWriter.CreateAsync(directory, Header, CancellationToken.None);
        await using (writer)
        {
            await Assert.ThrowsAsync<InvalidOperationException>(
                () => writer.AppendPartitionAsync(partitionPath, new SettlementTrailer(3, 6.03m), CancellationToken.None));
        }

        Assert.Empty(Directory.GetFiles(directory));
    }

    private async Task<SettlementFileResult> WriteAsync(string name, Func<SettlementFileWriter, Task> write)
    {
        SettlementFileWriter writer = await SettlementFileWriter.CreateAsync(
            Path.Combine(_outputDirectory, name),
            Header,
            CancellationToken.None);
        await using (writer)
        {
            await write(writer);
            return await writer.CompleteAsync(CancellationToken.None);
        }
    }

    private static SettlementRecord Record(int number)
    {
        return new SettlementRecord(
            Guid.CreateVersion7(),
            Guid.CreateVersion7(),
            number + 0.01m,
            new DateTimeOffset(2026, 11, 20, 0, 0, 0, TimeSpan.Zero).AddSeconds(number));
    }
}
