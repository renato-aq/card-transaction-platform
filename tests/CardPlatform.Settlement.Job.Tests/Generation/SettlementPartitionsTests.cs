using CardPlatform.Settlement.Job.Generation;

namespace CardPlatform.Settlement.Job.Tests.Generation;

public sealed class SettlementPartitionsTests
{
    [Fact]
    public void Merchants_are_split_into_contiguous_ranges_whose_sizes_differ_by_at_most_one()
    {
        IReadOnlyList<SettlementPartition> partitions = SettlementPartitions.Split(merchantCount: 10, parallelism: 4);

        Assert.Equal(
            [new SettlementPartition(0, 0, 3), new SettlementPartition(1, 3, 3), new SettlementPartition(2, 6, 2), new SettlementPartition(3, 8, 2)],
            partitions);
    }

    [Theory]
    [InlineData(1, 1)]
    [InlineData(1, 8)]
    [InlineData(3, 8)]
    [InlineData(8, 8)]
    [InlineData(9, 8)]
    [InlineData(1000, 2)]
    [InlineData(1000, 3)]
    [InlineData(1001, 4)]
    [InlineData(1000, 1)]
    public void Every_merchant_is_in_exactly_one_partition_in_the_order_of_the_list(int merchantCount, int parallelism)
    {
        IReadOnlyList<SettlementPartition> partitions = SettlementPartitions.Split(merchantCount, parallelism);

        Assert.Equal(Math.Min(merchantCount, parallelism), partitions.Count);
        Assert.Equal(Enumerable.Range(0, partitions.Count), partitions.Select(partition => partition.Index));
        Assert.All(partitions, partition => Assert.True(partition.MerchantCount >= 1));
        Assert.True(partitions.Max(partition => partition.MerchantCount) - partitions.Min(partition => partition.MerchantCount) <= 1);

        // Concatenated in the order of their index, the ranges list every position of the merchant list once, in order.
        Assert.Equal(
            Enumerable.Range(0, merchantCount),
            partitions.SelectMany(partition => Enumerable.Range(partition.FirstMerchant, partition.MerchantCount)));
    }

    [Fact]
    public void No_merchant_gives_no_partition()
    {
        Assert.Empty(SettlementPartitions.Split(merchantCount: 0, parallelism: 4));
    }

    [Fact]
    public void A_parallelism_below_1_is_rejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => SettlementPartitions.Split(merchantCount: 5, parallelism: 0));
    }
}
