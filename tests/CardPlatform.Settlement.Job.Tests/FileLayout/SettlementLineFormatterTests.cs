using System.Buffers;
using System.Text;

using CardPlatform.Settlement.Job.FileLayout;

namespace CardPlatform.Settlement.Job.Tests.FileLayout;

public class SettlementLineFormatterTests
{
    private static readonly SettlementHeader BrlHeader = new(new DateOnly(2026, 11, 26), "BRL");

    private static readonly Guid TransactionId = Guid.Parse("0193a3b0-5f1e-7c2d-8e4f-0123456789ab");

    private static readonly Guid MerchantAccountId = Guid.Parse("11111111-2222-3333-4444-555555555555");

    private static readonly DateTimeOffset TransactedAt =
        new DateTimeOffset(2026, 11, 26, 13, 45, 7, TimeSpan.Zero).AddTicks(1_234_560);

    private static SettlementDetail Detail(long sequenceNumber, decimal amount)
    {
        return new SettlementDetail(sequenceNumber, TransactionId, MerchantAccountId, amount, TransactedAt);
    }

    private static string HeaderLine(SettlementHeader header)
    {
        byte[] buffer = new byte[SettlementFileLayout.LineLength];
        int written = new SettlementLineFormatter(header).WriteHeader(buffer);
        return Encoding.ASCII.GetString(buffer, 0, written);
    }

    private static string DetailLine(SettlementHeader header, SettlementDetail detail)
    {
        byte[] buffer = new byte[SettlementFileLayout.LineLength];
        int written = new SettlementLineFormatter(header).WriteDetail(detail, buffer);
        return Encoding.ASCII.GetString(buffer, 0, written);
    }

    private static string TrailerLine(SettlementHeader header, SettlementTrailer trailer)
    {
        byte[] buffer = new byte[SettlementFileLayout.LineLength];
        int written = new SettlementLineFormatter(header).WriteTrailer(trailer, buffer);
        return Encoding.ASCII.GetString(buffer, 0, written);
    }

    // Formats a whole file (header, details, trailer accumulated from the details) the way the job streams it.
    private static byte[] FormatFile(SettlementHeader header, IEnumerable<SettlementDetail> details)
    {
        SettlementLineFormatter formatter = new(header);
        ArrayBufferWriter<byte> output = new();
        SettlementTrailer trailer = SettlementTrailer.Empty;

        output.Advance(formatter.WriteHeader(output.GetSpan(SettlementFileLayout.LineLength)));
        foreach (SettlementDetail detail in details)
        {
            output.Advance(formatter.WriteDetail(detail, output.GetSpan(SettlementFileLayout.LineLength)));
            trailer = trailer.Add(detail);
        }

        output.Advance(formatter.WriteTrailer(trailer, output.GetSpan(SettlementFileLayout.LineLength)));
        return output.WrittenSpan.ToArray();
    }

    private static List<SettlementDetail> SampleDetails()
    {
        return
        [
            Detail(1, 1234.56m),
            Detail(2, 0.01m),
            Detail(3, 100m),
            Detail(4, 9_999_999_999_999.99m),
        ];
    }

    [Fact]
    public void Header_line_has_record_type_date_currency_and_space_filler()
    {
        string line = HeaderLine(BrlHeader);

        string expected = "0" + "20261126" + "BRL" + new string(' ', 105) + "\n";
        Assert.Equal(expected, line);
    }

    [Fact]
    public void Detail_line_has_every_field_at_its_position()
    {
        string line = DetailLine(BrlHeader, Detail(1, 1234.56m));

        string expected = "1"
            + "000000001"
            + "0193a3b0-5f1e-7c2d-8e4f-0123456789ab"
            + "11111111-2222-3333-4444-555555555555"
            + "000000000123456"
            + "20261126134507123456"
            + "\n";
        Assert.Equal(expected, line);
    }

    [Fact]
    public void Trailer_line_has_record_type_count_total_and_space_filler()
    {
        string line = TrailerLine(BrlHeader, new SettlementTrailer(3, 1334.57m));

        string expected = "9" + "000000003" + "000000000000133457" + new string(' ', 89) + "\n";
        Assert.Equal(expected, line);
    }

    [Fact]
    public void Detail_instant_is_written_in_utc()
    {
        SettlementDetail detail = Detail(1, 1m) with
        {
            TransactedAt = new DateTimeOffset(2026, 11, 26, 22, 30, 0, TimeSpan.FromHours(-3)),
        };

        string line = DetailLine(BrlHeader, detail);

        SettlementField field = SettlementFileLayout.Detail.TransactedAt;
        Assert.Equal("20261127013000000000", line.Substring(field.Offset, field.Width));
    }

    [Theory]
    [InlineData("JPY", 1500, "000000000001500")]
    [InlineData("BRL", 15, "000000000001500")]
    [InlineData("KWD", 1.5, "000000000001500")]
    public void Amount_is_written_in_minor_units_of_the_file_currency(
        string currency, decimal amount, string expected)
    {
        SettlementHeader header = BrlHeader with { Currency = currency };

        string line = DetailLine(header, Detail(1, amount));

        SettlementField field = SettlementFileLayout.Detail.Amount;
        Assert.Equal(expected, line.Substring(field.Offset, field.Width));
    }

    [Fact]
    public void Every_line_of_a_file_has_the_same_length_and_ends_with_a_line_feed()
    {
        string file = Encoding.ASCII.GetString(FormatFile(BrlHeader, SampleDetails()));

        Assert.EndsWith("\n", file, StringComparison.Ordinal);
        string[] lines = file[..^1].Split('\n');
        Assert.Equal(6, lines.Length);
        Assert.All(lines, line => Assert.Equal(SettlementFileLayout.RecordLength, line.Length));
        Assert.Equal('0', lines[0][0]);
        Assert.All(lines[1..^1], line => Assert.Equal('1', line[0]));
        Assert.Equal('9', lines[^1][0]);
    }

    [Fact]
    public void File_is_pure_ascii()
    {
        byte[] file = FormatFile(BrlHeader, SampleDetails());

        Assert.All(file, b => Assert.True(b < 0x80));
    }

    [Fact]
    public void Trailer_carries_the_count_and_sum_of_the_details()
    {
        SettlementTrailer trailer = SampleDetails().Aggregate(SettlementTrailer.Empty, (total, detail) => total.Add(detail));

        Assert.Equal(4, trailer.DetailCount);
        Assert.Equal(10_000_000_001_334.56m, trailer.TotalAmount);

        string file = Encoding.ASCII.GetString(FormatFile(BrlHeader, SampleDetails()));
        string trailerLine = file[..^1].Split('\n')[^1];
        Assert.Equal("9" + "000000004" + "001000000000133456", trailerLine[..28]);
    }

    [Fact]
    public void Empty_file_has_a_zero_trailer()
    {
        string file = Encoding.ASCII.GetString(FormatFile(BrlHeader, []));

        string[] lines = file[..^1].Split('\n');
        Assert.Equal(2, lines.Length);
        Assert.Equal("9" + "000000000" + "000000000000000000", lines[1][..28]);
    }

    [Fact]
    public void Formatting_the_same_input_twice_produces_identical_bytes()
    {
        byte[] first = FormatFile(BrlHeader, SampleDetails());
        byte[] second = FormatFile(BrlHeader, SampleDetails());

        Assert.Equal(first, second);
    }

    [Fact]
    public void Amount_wider_than_its_field_throws()
    {
        // 10 trillion reais is 1 followed by 15 zeros in cents: 16 digits for a 15-digit field.
        SettlementDetail detail = Detail(1, 10_000_000_000_000m);

        Assert.Throws<ArgumentOutOfRangeException>(() => DetailLine(BrlHeader, detail));
    }

    [Fact]
    public void Sequence_number_wider_than_its_field_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetailLine(BrlHeader, Detail(1_000_000_000, 1m)));
    }

    [Fact]
    public void Total_amount_wider_than_its_field_throws()
    {
        SettlementTrailer trailer = new(1, 10_000_000_000_000_000m);

        Assert.Throws<ArgumentOutOfRangeException>(() => TrailerLine(BrlHeader, trailer));
    }

    [Fact]
    public void Amount_with_more_decimal_places_than_the_currency_allows_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetailLine(BrlHeader, Detail(1, 1.005m)));
    }

    [Fact]
    public void Negative_amount_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetailLine(BrlHeader, Detail(1, -1m)));
    }

    [Fact]
    public void Sequence_number_below_one_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DetailLine(BrlHeader, Detail(0, 1m)));
    }

    [Fact]
    public void Instant_finer_than_a_microsecond_throws()
    {
        SettlementDetail detail = Detail(1, 1m) with { TransactedAt = TransactedAt.AddTicks(1) };

        Assert.Throws<ArgumentOutOfRangeException>(() => DetailLine(BrlHeader, detail));
    }

    [Theory]
    [InlineData("brl")]
    [InlineData("BR")]
    [InlineData("BRLX")]
    public void Invalid_currency_code_is_rejected(string currency)
    {
        Assert.Throws<ArgumentException>(() => new SettlementLineFormatter(BrlHeader with { Currency = currency }));
    }

    [Fact]
    public void Destination_smaller_than_a_line_is_rejected()
    {
        SettlementLineFormatter formatter = new(BrlHeader);

        Assert.Throws<ArgumentException>(() => formatter.WriteHeader(new byte[SettlementFileLayout.LineLength - 1]));
    }
}
