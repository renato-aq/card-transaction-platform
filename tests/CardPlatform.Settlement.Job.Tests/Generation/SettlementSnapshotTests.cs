using CardPlatform.Settlement.Job.Generation;

namespace CardPlatform.Settlement.Job.Tests.Generation;

public sealed class SettlementSnapshotTests
{
    [Theory]
    [InlineData("00000003-0000001B-1")]
    [InlineData("00000004-00000002-1")]
    public void Snapshot_ids_returned_by_postgresql_are_recognized(string snapshotId)
    {
        Assert.True(SettlementSnapshot.IsSnapshotId(snapshotId));
    }

    // The id is written into SET TRANSACTION SNAPSHOT, which takes no parameter: nothing else may pass.
    [Theory]
    [InlineData("")]
    [InlineData("00000003")]
    [InlineData("00000003-0000001B-1'; DROP TABLE entries; --")]
    [InlineData("00000003-0000001b-1")]
    [InlineData("00000003-0000001B-1 ")]
    [InlineData("00000003-0000001B-1\n")]
    [InlineData("00000003--1")]
    public void Anything_else_is_not_a_snapshot_id(string value)
    {
        Assert.False(SettlementSnapshot.IsSnapshotId(value));
    }
}
