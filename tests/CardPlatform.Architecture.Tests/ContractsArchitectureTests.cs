using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

using CardPlatform.Contracts;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;

namespace CardPlatform.Architecture.Tests;

/// <summary>
/// <c>CardPlatform.Contracts</c> is shared by every producer and consumer of integration events, so it stays plain:
/// no service code, no Kafka and no EF Core (the messaging plumbing lives elsewhere).
/// </summary>
public sealed class ContractsArchitectureTests
{
    private static readonly System.Reflection.Assembly ContractsAssembly = typeof(EventEnvelope).Assembly;

    private static readonly ArchUnitArchitecture Architecture = new ArchLoader()
        .LoadAssemblies(ContractsAssembly)
        .Build();

    private static readonly IObjectProvider<IType> Contracts =
        Types().That().ResideInAssembly(ContractsAssembly).As("Contracts");

    // Namespace patterns match the root namespace and every sub-namespace.
    private static readonly IObjectProvider<IType> Services =
        Types(includeReferenced: true)
            .That().ResideInNamespaceMatching(@"^CardPlatform\.(Authorization|Fraud|Ledger|Settlement)(\..+)?$")
            .As("service projects");

    private static readonly IObjectProvider<IType> Kafka =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Confluent\.Kafka(\..+)?$").As("Kafka");

    private static readonly IObjectProvider<IType> EntityFrameworkCore =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Microsoft\.EntityFrameworkCore(\..+)?$").As("EF Core");

    // Assembly-level names of the same dependencies, checked on the compiled references as a second net.
    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "CardPlatform.Authorization",
        "CardPlatform.Fraud",
        "CardPlatform.Ledger",
        "CardPlatform.Settlement",
        "Confluent.Kafka",
        "Microsoft.EntityFrameworkCore",
    ];

    [Fact]
    public void Contracts_does_not_depend_on_service_projects()
    {
        Types().That().Are(Contracts).Should().NotDependOnAny(Services).Check(Architecture);
    }

    [Fact]
    public void Contracts_does_not_depend_on_kafka()
    {
        Types().That().Are(Contracts).Should().NotDependOnAny(Kafka).Check(Architecture);
    }

    [Fact]
    public void Contracts_does_not_depend_on_entity_framework_core()
    {
        Types().That().Are(Contracts).Should().NotDependOnAny(EntityFrameworkCore).Check(Architecture);
    }

    [Fact]
    public void Contracts_does_not_reference_service_kafka_or_ef_core_assemblies()
    {
        string[] forbidden =
        [
            .. ContractsAssembly.GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)
                .Where(name => ForbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))),
        ];

        Assert.Empty(forbidden);
    }
}
