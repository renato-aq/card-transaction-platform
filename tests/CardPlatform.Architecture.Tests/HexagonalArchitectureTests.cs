using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnit;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace CardPlatform.Architecture.Tests;

/// <summary>
/// Enforces the dependency rule of ADR 0004 (hexagonal architecture). Each service that follows it derives from
/// this class and supplies its <see cref="ServiceLayers"/>, so every service is checked against the same rules.
/// </summary>
public abstract class HexagonalArchitectureTests(ServiceLayers layers)
{
    // Namespace patterns match the root namespace and every sub-namespace (e.g. Microsoft.AspNetCore.Http).
    private static readonly IObjectProvider<IType> EntityFrameworkCore =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Microsoft\.EntityFrameworkCore(\..+)?$").As("EF Core");

    private static readonly IObjectProvider<IType> AspNetCore =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Microsoft\.AspNetCore(\..+)?$").As("ASP.NET Core");

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
    public void Domain_does_not_depend_on_api()
    {
        Types().That().Are(layers.DomainLayer).Should().NotDependOnAny(layers.ApiLayer)
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
    public void Application_does_not_depend_on_infrastructure()
    {
        Types().That().Are(layers.ApplicationLayer).Should().NotDependOnAny(layers.InfrastructureLayer)
            .Check(layers.Architecture);
    }

    [Fact]
    public void Application_does_not_depend_on_api()
    {
        Types().That().Are(layers.ApplicationLayer).Should().NotDependOnAny(layers.ApiLayer)
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
}
