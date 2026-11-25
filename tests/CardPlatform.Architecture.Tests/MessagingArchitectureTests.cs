using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

using CardPlatform.Messaging.Outbox;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;

namespace CardPlatform.Architecture.Tests;

/// <summary>
/// <c>CardPlatform.Messaging</c> holds the messaging plumbing shared by the services (the outbox table and, later, the
/// Kafka publisher). It is used by their Infrastructure layers, so it must not know any service, nor the web stack.
/// </summary>
public sealed class MessagingArchitectureTests
{
    private static readonly System.Reflection.Assembly MessagingAssembly = typeof(OutboxMessage).Assembly;

    private static readonly ArchUnitArchitecture Architecture = new ArchLoader()
        .LoadAssemblies(MessagingAssembly)
        .Build();

    private static readonly IObjectProvider<IType> Messaging =
        Types().That().ResideInAssembly(MessagingAssembly).As("Messaging");

    // Namespace patterns match the root namespace and every sub-namespace.
    private static readonly IObjectProvider<IType> Services =
        Types(includeReferenced: true)
            .That().ResideInNamespaceMatching(@"^CardPlatform\.(Authorization|Fraud|Ledger|Settlement)(\..+)?$")
            .As("service projects");

    private static readonly IObjectProvider<IType> AspNetCore =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Microsoft\.AspNetCore(\..+)?$").As("ASP.NET Core");

    private static readonly string[] ForbiddenAssemblyPrefixes =
    [
        "CardPlatform.Authorization",
        "CardPlatform.Fraud",
        "CardPlatform.Ledger",
        "CardPlatform.Settlement",
        "Microsoft.AspNetCore",
    ];

    [Fact]
    public void Messaging_does_not_depend_on_service_projects()
    {
        Types().That().Are(Messaging).Should().NotDependOnAny(Services).Check(Architecture);
    }

    [Fact]
    public void Messaging_does_not_depend_on_aspnet_core()
    {
        Types().That().Are(Messaging).Should().NotDependOnAny(AspNetCore).Check(Architecture);
    }

    [Fact]
    public void Messaging_does_not_reference_service_or_aspnet_core_assemblies()
    {
        string[] forbidden =
        [
            .. MessagingAssembly.GetReferencedAssemblies()
                .Select(assembly => assembly.Name ?? string.Empty)
                .Where(name => ForbiddenAssemblyPrefixes.Any(prefix => name.StartsWith(prefix, StringComparison.Ordinal))),
        ];

        Assert.Empty(forbidden);
    }
}
