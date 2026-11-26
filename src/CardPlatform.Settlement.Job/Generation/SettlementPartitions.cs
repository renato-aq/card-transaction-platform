namespace CardPlatform.Settlement.Job.Generation;

/// <summary>
/// One partition of a parallel run: a contiguous range of the merchant accounts, in the order of the file, as returned
/// by the database (<see cref="SettlementPageReader.MerchantsAsync"/>).
/// </summary>
/// <param name="Index">Position of the partition in the file, from 0.</param>
/// <param name="FirstMerchant">Position of its first merchant in the list of merchants, from 0.</param>
/// <param name="MerchantCount">Number of merchants in the partition, at least 1.</param>
public readonly record struct SettlementPartition(int Index, int FirstMerchant, int MerchantCount);

/// <summary>
/// Splits the merchant accounts of a run into partitions for <see cref="OptimizedSettlementFileGenerator"/>: contiguous,
/// disjoint ranges of the merchant list in the order of the file, so every entry of a merchant falls in one partition, and
/// the partitions concatenated in the order of their <see cref="SettlementPartition.Index"/> list every merchant once, in
/// the order of the file. No merchant id is compared in memory: the ranges are positions in the list the database sorted.
/// </summary>
/// <remarks>
/// The ranges hold as many merchants as possible each, their sizes differing by at most one, the larger ones first.
/// There are never more partitions than merchants, so no partition is empty, and none at all without merchants. The
/// ranges balance the number of merchants, not of entries: a merchant holding most of the day's entries makes its
/// partition the longest one.
/// </remarks>
public static class SettlementPartitions
{
    /// <summary>Splits <paramref name="merchantCount"/> merchants into at most <paramref name="parallelism"/> partitions.</summary>
    public static IReadOnlyList<SettlementPartition> Split(int merchantCount, int parallelism)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(merchantCount);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(parallelism);

        int partitionCount = Math.Min(merchantCount, parallelism);
        if (partitionCount == 0)
        {
            return [];
        }

        int smallerSize = merchantCount / partitionCount;
        int largerPartitions = merchantCount % partitionCount;

        SettlementPartition[] partitions = new SettlementPartition[partitionCount];
        int firstMerchant = 0;
        for (int index = 0; index < partitionCount; index++)
        {
            int size = index < largerPartitions ? smallerSize + 1 : smallerSize;
            partitions[index] = new SettlementPartition(index, firstMerchant, size);
            firstMerchant += size;
        }

        return partitions;
    }
}
