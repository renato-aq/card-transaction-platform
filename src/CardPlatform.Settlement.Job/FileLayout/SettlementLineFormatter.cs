using System.Globalization;

namespace CardPlatform.Settlement.Job.FileLayout;

/// <summary>
/// Writes the lines of one settlement file (one settlement date, one currency) as ASCII bytes, following
/// <see cref="SettlementFileLayout"/>. Each call writes exactly <see cref="SettlementFileLayout.LineLength"/> bytes,
/// terminator included, into a caller-provided buffer, so a file can be streamed line by line without building it in
/// memory. The output depends only on the inputs. A value that does not fit its field throws
/// <see cref="ArgumentOutOfRangeException"/>; nothing is ever truncated. The formatter holds no mutable state and
/// can be shared between threads.
/// </summary>
public sealed class SettlementLineFormatter
{
    private const byte Space = (byte)' ';

    private const int CurrencyCodeLength = 3;

    private readonly SettlementHeader _header;

    private readonly decimal _minorUnitsPerMajorUnit;

    public SettlementLineFormatter(SettlementHeader header)
    {
        ArgumentNullException.ThrowIfNull(header);

        if (header.Currency is not { Length: CurrencyCodeLength } || !header.Currency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException(
                $"Currency '{header.Currency}' is not a valid ISO 4217 code (three uppercase letters).",
                nameof(header));
        }

        _header = header;
        _minorUnitsPerMajorUnit = Pow10(CurrencyMinorUnits.GetExponent(header.Currency));
    }

    /// <summary>Writes the header line into <paramref name="destination"/> and returns the bytes written.</summary>
    public int WriteHeader(Span<byte> destination)
    {
        Span<byte> line = StartLine(destination, SettlementFileLayout.HeaderRecordType);

        Span<byte> date = Field(line, SettlementFileLayout.Header.SettlementDate);
        WriteDigits(date[..4], (ulong)_header.SettlementDate.Year, nameof(_header), "settlement date");
        WriteDigits(date.Slice(4, 2), (ulong)_header.SettlementDate.Month, nameof(_header), "settlement date");
        WriteDigits(date.Slice(6, 2), (ulong)_header.SettlementDate.Day, nameof(_header), "settlement date");

        Span<byte> currency = Field(line, SettlementFileLayout.Header.Currency);
        for (int i = 0; i < _header.Currency.Length; i++)
        {
            currency[i] = (byte)_header.Currency[i];
        }

        return SettlementFileLayout.LineLength;
    }

    /// <summary>Writes the detail line of <paramref name="detail"/> into <paramref name="destination"/> and returns the bytes written.</summary>
    public int WriteDetail(SettlementDetail detail, Span<byte> destination)
    {
        ArgumentNullException.ThrowIfNull(detail);

        if (detail.SequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(detail), detail.SequenceNumber, "The sequence number starts at 1.");
        }

        Span<byte> line = StartLine(destination, SettlementFileLayout.DetailRecordType);

        WriteSequenceNumberField(line, detail.SequenceNumber, nameof(detail));
        WriteGuid(Field(line, SettlementFileLayout.Detail.TransactionId), detail.TransactionId);
        WriteGuid(Field(line, SettlementFileLayout.Detail.MerchantAccountId), detail.MerchantAccountId);
        WriteDigits(
            Field(line, SettlementFileLayout.Detail.Amount),
            ToMinorUnits(detail.Amount, nameof(detail), "amount"),
            nameof(detail),
            "amount");
        WriteInstant(Field(line, SettlementFileLayout.Detail.TransactedAt), detail.TransactedAt, nameof(detail));

        return SettlementFileLayout.LineLength;
    }

    /// <summary>
    /// Replaces the sequence number of the detail line at the start of <paramref name="detailLine"/>, written by
    /// <see cref="WriteDetail"/>, with <paramref name="sequenceNumber"/>, in the same format; the rest of the line is left
    /// as it is. Lets details formatted before their position in the file was known be numbered when they are placed.
    /// </summary>
    public static void RenumberDetail(long sequenceNumber, Span<byte> detailLine)
    {
        if (sequenceNumber < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(sequenceNumber), sequenceNumber, "The sequence number starts at 1.");
        }

        if (detailLine.Length < SettlementFileLayout.LineLength
            || detailLine[SettlementFileLayout.RecordType.Offset] != SettlementFileLayout.DetailRecordType
            || detailLine[SettlementFileLayout.RecordLength] != SettlementFileLayout.LineTerminator)
        {
            throw new ArgumentException("The line is not a detail line of the settlement file.", nameof(detailLine));
        }

        WriteSequenceNumberField(detailLine, sequenceNumber, nameof(sequenceNumber));
    }

    /// <summary>Writes the trailer line of <paramref name="trailer"/> into <paramref name="destination"/> and returns the bytes written.</summary>
    public int WriteTrailer(SettlementTrailer trailer, Span<byte> destination)
    {
        if (trailer.DetailCount < 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(trailer), trailer.DetailCount, "The detail count cannot be negative.");
        }

        Span<byte> line = StartLine(destination, SettlementFileLayout.TrailerRecordType);

        WriteDigits(
            Field(line, SettlementFileLayout.Trailer.DetailCount),
            (ulong)trailer.DetailCount,
            nameof(trailer),
            "detail count");
        WriteDigits(
            Field(line, SettlementFileLayout.Trailer.TotalAmount),
            ToMinorUnits(trailer.TotalAmount, nameof(trailer), "total amount"),
            nameof(trailer),
            "total amount");

        return SettlementFileLayout.LineLength;
    }

    private static Span<byte> StartLine(Span<byte> destination, byte recordType)
    {
        if (destination.Length < SettlementFileLayout.LineLength)
        {
            throw new ArgumentException(
                $"The destination must have room for {SettlementFileLayout.LineLength} bytes.", nameof(destination));
        }

        Span<byte> line = destination[..SettlementFileLayout.LineLength];
        line[..SettlementFileLayout.RecordLength].Fill(Space);
        line[SettlementFileLayout.RecordType.Offset] = recordType;
        line[SettlementFileLayout.RecordLength] = SettlementFileLayout.LineTerminator;
        return line;
    }

    private static void WriteSequenceNumberField(Span<byte> line, long sequenceNumber, string paramName)
    {
        WriteDigits(
            Field(line, SettlementFileLayout.Detail.SequenceNumber),
            (ulong)sequenceNumber,
            paramName,
            "sequence number");
    }

    private static Span<byte> Field(Span<byte> line, SettlementField field)
    {
        return line.Slice(field.Offset, field.Width);
    }

    // Writes the value right-aligned and zero-padded; a value with more digits than the target is an error.
    private static void WriteDigits(Span<byte> target, ulong value, string paramName, string fieldName)
    {
        ulong remaining = value;
        for (int i = target.Length - 1; i >= 0; i--)
        {
            target[i] = (byte)('0' + (int)(remaining % 10));
            remaining /= 10;
        }

        if (remaining != 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"The {fieldName} {value.ToString(CultureInfo.InvariantCulture)} does not fit in {target.Length} digits.");
        }
    }

    private static void WriteGuid(Span<byte> target, Guid value)
    {
        // The "D" format is always 36 ASCII characters (lowercase hex and hyphens), the exact width of the field.
        bool written = value.TryFormat(target, out int bytesWritten, "D");
        if (!written || bytesWritten != target.Length)
        {
            throw new InvalidOperationException("A GUID field must be exactly 36 characters wide.");
        }
    }

    private static void WriteInstant(Span<byte> target, DateTimeOffset value, string paramName)
    {
        DateTime utc = value.UtcDateTime;
        long ticksInSecond = utc.Ticks % TimeSpan.TicksPerSecond;
        if (ticksInSecond % TimeSpan.TicksPerMicrosecond != 0)
        {
            throw new ArgumentOutOfRangeException(
                paramName, value, "The transaction instant has a precision finer than a microsecond.");
        }

        const string fieldName = "transaction instant";
        WriteDigits(target[..4], (ulong)utc.Year, paramName, fieldName);
        WriteDigits(target.Slice(4, 2), (ulong)utc.Month, paramName, fieldName);
        WriteDigits(target.Slice(6, 2), (ulong)utc.Day, paramName, fieldName);
        WriteDigits(target.Slice(8, 2), (ulong)utc.Hour, paramName, fieldName);
        WriteDigits(target.Slice(10, 2), (ulong)utc.Minute, paramName, fieldName);
        WriteDigits(target.Slice(12, 2), (ulong)utc.Second, paramName, fieldName);
        WriteDigits(target.Slice(14, 6), (ulong)(ticksInSecond / TimeSpan.TicksPerMicrosecond), paramName, fieldName);
    }

    private ulong ToMinorUnits(decimal amount, string paramName, string fieldName)
    {
        if (amount < 0)
        {
            throw new ArgumentOutOfRangeException(paramName, amount, $"The {fieldName} cannot be negative.");
        }

        decimal minorUnits;
        try
        {
            minorUnits = amount * _minorUnitsPerMajorUnit;
        }
        catch (OverflowException exception)
        {
            throw new ArgumentOutOfRangeException(
                $"The {fieldName} {amount.ToString(CultureInfo.InvariantCulture)} is too large.", exception);
        }

        if (minorUnits != decimal.Truncate(minorUnits))
        {
            throw new ArgumentOutOfRangeException(
                paramName,
                amount,
                $"The {fieldName} has more decimal places than {_header.Currency} minor units allow.");
        }

        if (minorUnits > ulong.MaxValue)
        {
            throw new ArgumentOutOfRangeException(
                paramName, amount, $"The {fieldName} {amount.ToString(CultureInfo.InvariantCulture)} is too large.");
        }

        return decimal.ToUInt64(minorUnits);
    }

    private static decimal Pow10(int exponent)
    {
        decimal result = 1m;
        for (int i = 0; i < exponent; i++)
        {
            result *= 10m;
        }

        return result;
    }
}
