using System.Buffers;
using System.Globalization;
using System.Security.Cryptography;
using System.Text.Json;

using CardPlatform.Authorization.Application.Authorizations;

namespace CardPlatform.Authorization.Application.Idempotency;

/// <summary>
/// SHA-256 (lowercase hex) of the canonical JSON of the contract fields of a purchase, so that property order,
/// whitespace, letter case of the GUIDs or trailing zeros of the amount do not change it (ADR 0008). The canonical
/// JSON has the properties in alphabetical order, no whitespace, every value as a string or <c>null</c>: GUIDs in the
/// <c>D</c> format, the amount without trailing zeros (<c>30.00</c> → <c>"30"</c>) and the currency exactly as sent.
/// Only the request is hashed: <see cref="AuthorizePurchase.AuthorizationId"/> is ignored.
/// </summary>
public static class PurchaseRequestHash
{
    public static string Compute(AuthorizePurchase purchase)
    {
        ArgumentNullException.ThrowIfNull(purchase);

        ArrayBufferWriter<byte> buffer = new();
        using (Utf8JsonWriter writer = new(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("amount", purchase.Amount?.ToString("0.############################", CultureInfo.InvariantCulture));
            writer.WriteString("cardId", purchase.CardId?.ToString("D"));
            writer.WriteString("currency", purchase.Currency);
            writer.WriteString("merchantAccountId", purchase.MerchantAccountId?.ToString("D"));
            writer.WriteEndObject();
        }

        return Convert.ToHexStringLower(SHA256.HashData(buffer.WrittenSpan));
    }
}
