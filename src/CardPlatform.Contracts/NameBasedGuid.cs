using System.Security.Cryptography;
using System.Text;

namespace CardPlatform.Contracts;

/// <summary>
/// Name-based UUIDs, version 5 (RFC 9562, section 5.5): the same namespace and name always give the same id, so
/// anything derived from stable data gets a stable id without storing it. Producers use it for deterministic event ids
/// (e.g. one per authorization and event type), so an event written more than once carries the same id and consumers
/// keep only the first copy (ADR 0010).
/// </summary>
public static class NameBasedGuid
{
    private const int GuidLength = 16;

    /// <summary>The UUID v5 of <paramref name="name"/> (UTF-8) in <paramref name="namespaceId"/>.</summary>
    public static Guid Create(Guid namespaceId, string name)
    {
        ArgumentNullException.ThrowIfNull(name);

        byte[] nameBytes = Encoding.UTF8.GetBytes(name);
        byte[] input = new byte[GuidLength + nameBytes.Length];
        if (!namespaceId.TryWriteBytes(input, bigEndian: true, out _))
        {
            throw new InvalidOperationException("Could not write the namespace id.");
        }

        nameBytes.CopyTo(input, GuidLength);

        // SHA-1 is what version 5 is defined with; it identifies, it does not protect anything.
#pragma warning disable CA5350
        byte[] hash = SHA1.HashData(input);
#pragma warning restore CA5350

        Span<byte> bytes = hash.AsSpan(0, GuidLength);
        bytes[6] = (byte)((bytes[6] & 0x0F) | 0x50); // version 5
        bytes[8] = (byte)((bytes[8] & 0x3F) | 0x80); // RFC 9562 variant
        return new Guid(bytes, bigEndian: true);
    }
}
