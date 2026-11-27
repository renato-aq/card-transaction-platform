namespace CardPlatform.Settlement.Job.FileLayout;

/// <summary>
/// The single declaration of the daily settlement file layout: a fixed-width ASCII file in which every record has
/// <see cref="RecordLength"/> characters followed by <see cref="LineTerminator"/> (the last line included).
/// The first character identifies the record type; characters not covered by a field are filled with spaces.
/// </summary>
public static class SettlementFileLayout
{
    /// <summary>Characters in a record, excluding the line terminator.</summary>
    public const int RecordLength = 117;

    /// <summary>Bytes in a line: the record plus the line terminator.</summary>
    public const int LineLength = RecordLength + 1;

    public const byte LineTerminator = (byte)'\n';

    public const byte HeaderRecordType = (byte)'0';

    public const byte DetailRecordType = (byte)'1';

    public const byte TrailerRecordType = (byte)'9';

    /// <summary>The record type, first character of every record.</summary>
    public static readonly SettlementField RecordType = new(0, 1);

    /// <summary>Header record (<c>0</c>): one per file, first line.</summary>
    public static class Header
    {
        /// <summary>Settlement date, <c>yyyyMMdd</c>.</summary>
        public static readonly SettlementField SettlementDate = new(1, 8);

        /// <summary>ISO 4217 currency code, three uppercase letters.</summary>
        public static readonly SettlementField Currency = new(9, 3);
    }

    /// <summary>Detail record (<c>1</c>): one per settled transaction.</summary>
    public static class Detail
    {
        /// <summary>Sequence number of the detail line, starting at 1.</summary>
        public static readonly SettlementField SequenceNumber = new(1, 9);

        /// <summary>Transaction id, lowercase hyphenated GUID.</summary>
        public static readonly SettlementField TransactionId = new(10, 36);

        /// <summary>Merchant account id, lowercase hyphenated GUID.</summary>
        public static readonly SettlementField MerchantAccountId = new(46, 36);

        /// <summary>Amount in minor units of the file currency.</summary>
        public static readonly SettlementField Amount = new(82, 15);

        /// <summary>Transaction instant in UTC, <c>yyyyMMddHHmmssffffff</c> (microseconds).</summary>
        public static readonly SettlementField TransactedAt = new(97, 20);
    }

    /// <summary>Trailer record (<c>9</c>): one per file, last line.</summary>
    public static class Trailer
    {
        /// <summary>Number of detail records in the file.</summary>
        public static readonly SettlementField DetailCount = new(1, 9);

        /// <summary>Sum of the detail amounts, in minor units of the file currency.</summary>
        public static readonly SettlementField TotalAmount = new(10, 18);
    }
}
