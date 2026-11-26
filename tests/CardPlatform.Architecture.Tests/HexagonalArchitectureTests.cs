using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace CardPlatform.Architecture.Tests;

/// <summary>
/// Enforces the dependency rule of ADR 0004 (hexagonal architecture). Each service that follows it derives from
/// this class and supplies its <see cref="ServiceLayers"/>, so every service is checked against the same rules. The
/// host layer is the entry point of the service, whether an HTTP API (Ledger, Authorization) or a worker (Fraud): no
/// other layer depends on it.
/// </summary>
public abstract class HexagonalArchitectureTests(ServiceLayers layers)
{
    // Namespace patterns match the root namespace and every sub-namespace (e.g. Microsoft.AspNetCore.Http).
    private static readonly IObjectProvider<IType> EntityFrameworkCore =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Microsoft\.EntityFrameworkCore(\..+)?$").As("EF Core");

    private static readonly IObjectProvider<IType> AspNetCore =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Microsoft\.AspNetCore(\..+)?$").As("ASP.NET Core");

    private static readonly IObjectProvider<IType> HttpClient =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^System\.Net\.Http(\..+)?$").As("System.Net.Http");

    // The integration event contracts: mapped from the domain model by the Application, never used by the Domain.
    private static readonly IObjectProvider<IType> IntegrationContracts =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^CardPlatform\.Contracts(\..+)?$").As("CardPlatform.Contracts");

    // The messaging plumbing (outbox persistence, Kafka): an outbound adapter, used only by Infrastructure.
    private static readonly IObjectProvider<IType> Messaging =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^CardPlatform\.Messaging(\..+)?$").As("CardPlatform.Messaging");

    // The Kafka client: wrapped by CardPlatform.Messaging and wired by Infrastructure only (ADR 0010).
    private static readonly IObjectProvider<IType> Kafka =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Confluent\.Kafka(\..+)?$").As("Kafka");

    // Assembly-level names of the messaging dependencies, checked on the compiled references as a second net.
    private static readonly string[] MessagingAssemblyPrefixes = ["CardPlatform.Messaging", "Confluent.Kafka", "Aspire.Confluent.Kafka"];

    [Fact]
    public void Domain_does_not_depend_on_application()
    {
        Types().That().Are(layers.DomainLayer).Should().NotDependOnAny(layers.ApplicationLayer)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_infrastructure()
    {
        Types().That().Are(layers.DomainLayer).Should().NotDependOnAny(layers.InfrastructureLayer)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_host()
    {
        Types().That().Are(layers.DomainLayer).Should().NotDependOnAny(layers.HostLayer)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_entity_framework_core()
    {
        Types().That().Are(layers.DomainLayer).Should().NotDependOnAny(EntityFrameworkCore)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_aspnet_core()
    {
        Types().That().Are(layers.DomainLayer).Should().NotDependOnAny(AspNetCore)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_integration_contracts()
    {
        Types().That().Are(layers.DomainLayer).Should().NotDependOnAny(IntegrationContracts)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_messaging()
    {
        Types().That().Are(layers.DomainLayer).Should().NotDependOnAny(Messaging)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_messaging()
    {
        // The Application writes events through its own port (e.g. IOutbox); the outbox table is an adapter detail.
        Types().That().Are(layers.ApplicationLayer).Should().NotDependOnAny(Messaging)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Host_does_not_depend_on_messaging()
    {
        Types().That().Are(layers.HostLayer).Should().NotDependOnAny(Messaging)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_kafka()
    {
        Types().That().Are(layers.DomainLayer).Should().NotDependOnAny(Kafka)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_kafka()
    {
        Types().That().Are(layers.ApplicationLayer).Should().NotDependOnAny(Kafka)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Host_does_not_depend_on_kafka()
    {
        Types().That().Are(layers.HostLayer).Should().NotDependOnAny(Kafka)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Domain_and_application_do_not_reference_messaging_or_kafka_assemblies()
    {
        string[] forbidden =
        [
            .. new[] { layers.DomainAssembly, layers.ApplicationAssembly }
                .SelectMany(static assembly => assembly.GetReferencedAssemblies())
                .Select(static assembly => assembly.Name ?? string.Empty)
                .Where(static name => MessagingAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))),
        ];

        Assert.Empty(forbidden);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure()
    {
        Types().That().Are(layers.ApplicationLayer).Should().NotDependOnAny(layers.InfrastructureLayer)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_host()
    {
        Types().That().Are(layers.ApplicationLayer).Should().NotDependOnAny(layers.HostLayer)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Infrastructure_does_not_depend_on_host()
    {
        // The host composes the adapters; an adapter never reaches back into the composition root.
        Types().That().Are(layers.InfrastructureLayer).Should().NotDependOnAny(layers.HostLayer)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_entity_framework_core()
    {
        Types().That().Are(layers.ApplicationLayer).Should().NotDependOnAny(EntityFrameworkCore)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_aspnet_core()
    {
        Types().That().Are(layers.ApplicationLayer).Should().NotDependOnAny(AspNetCore)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_http_client()
    {
        // Outbound HTTP (e.g. the Authorization's Ledger client) is an Infrastructure adapter behind a port.
        Types().That().Are(layers.ApplicationLayer).Should().NotDependOnAny(HttpClient)
            .Check(layers.Architecture);
    }
}