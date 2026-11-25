using CardPlatform.Authorization.Application.Authorizations;
using CardPlatform.Authorization.Application.Idempotency;

namespace CardPlatform.Authorization.Application.Tests.Idempotency;

public class PurchaseRequestHashTests
{
    private static readonly Guid CardId = Guid.Parse("0196f1a4-0000-7000-8000-000000000001");
    private static readonly Guid MerchantAccountId = Guid.Parse("0196f1a4-0000-7000-8000-000000000002");

    [Fact]
    public void Hash_is_lowercase_hex_sha256()
    {
        string hash = PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30m, "BRL"));

        Assert.Equal(64, hash.Length);
        Assert.All(hash, static character => Assert.True(char.IsAsciiHexDigitLower(character) || char.IsAsciiDigit(character)));
    }

    [Fact]
    public void Trailing_zeros_of_the_amount_do_not_change_the_hash()
    {
        Assert.Equal(
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30m, "BRL")),
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30.00m, "BRL")));
    }

    [Fact]
    public void Reserved_authorization_id_does_not_change_the_hash()
    {
        Assert.Equal(
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30m, "BRL")),
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30m, "BRL", Guid.NewGuid())));
    }

    [Fact]
    public void Every_contract_field_changes_the_hash()
    {
        string[] hashes =
        [
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30m, "BRL")),
            PurchaseRequestHash.Compute(new AuthorizePurchase(Guid.NewGuid(), MerchantAccountId, 30m, "BRL")),
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, Guid.NewGuid(), 30m, "BRL")),
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30.01m, "BRL")),
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30m, "USD")),
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30m, "brl")),
            PurchaseRequestHash.Compute(new AuthorizePurchase(null, MerchantAccountId, 30m, "BRL")),
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, null, "BRL")),
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30m, null)),
        ];

        Assert.Equal(hashes.Length, hashes.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void Hash_is_stable()
    {
        // SHA-256 of {"amount":"30.5","cardId":"0196f1a4-0000-7000-8000-000000000001","currency":"BRL","merchantAccountId":"0196f1a4-0000-7000-8000-000000000002"}
        Assert.Equal(
            Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(
                "{\"amount\":\"30.5\",\"cardId\":\"0196f1a4-0000-7000-8000-000000000001\",\"currency\":\"BRL\","
                + "\"merchantAccountId\":\"0196f1a4-0000-7000-8000-000000000002\"}"))),
            PurchaseRequestHash.Compute(new AuthorizePurchase(CardId, MerchantAccountId, 30.50m, "BRL")));
    }
}
