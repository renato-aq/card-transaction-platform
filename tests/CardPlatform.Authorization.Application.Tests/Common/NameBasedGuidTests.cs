using CardPlatform.Authorization.Application.Common;

namespace CardPlatform.Authorization.Application.Tests.Common;

public class NameBasedGuidTests
{
    // Namespace ID for DNS names (RFC 9562, section 6.6).
    private static readonly Guid DnsNamespace = new("6ba7b810-9dad-11d1-80b4-00c04fd430c8");

    [Fact]
    public void Matches_the_rfc_9562_version_5_example()
    {
        // RFC 9562, appendix A.4: UUIDv5 of "www.example.com" in the DNS namespace.
        Assert.Equal(
            new Guid("2ed6657d-e927-568b-95e1-2665a8aea6a2"),
            NameBasedGuid.Create(DnsNamespace, "www.example.com"));
    }

    [Fact]
    public void Same_namespace_and_name_give_the_same_version_5_id()
    {
        Guid first = NameBasedGuid.Create(DnsNamespace, "card-platform");

        Assert.Equal(first, NameBasedGuid.Create(DnsNamespace, "card-platform"));
        Assert.Equal(5, first.Version);
        Assert.Equal(0b10, first.Variant >> 2);
    }

    [Fact]
    public void Other_name_or_namespace_gives_another_id()
    {
        Guid id = NameBasedGuid.Create(DnsNamespace, "card-platform");

        Assert.NotEqual(id, NameBasedGuid.Create(DnsNamespace, "card-platform2"));
        Assert.NotEqual(id, NameBasedGuid.Create(Guid.NewGuid(), "card-platform"));
    }
}
