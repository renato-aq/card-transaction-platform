using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnit;

using CardPlatform.Ledger.Application;
using CardPlatform.Ledger.Domain;
using CardPlatform.Ledger.Infrastructure;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;

namespace CardPlatform.Architecture.Tests;

/// <summary>
/// Enforces the dependency rule of ADR 0004 (hexagonal architecture) for the Ledger service.
/// </summary>
public class LedgerArchitectureTests
{
    private static readonly ArchUnitArchitecture LedgerArchitecture = new ArchLoader()
        .LoadAssemblies(
            typeof(DomainAssemblyMarker).Assembly,
            typeof(ApplicationAssemblyMarker).Assembly,
            typeof(InfrastructureAssemblyMarker).Assembly,
            typeof(Program).Assembly)
        .Build();

    private static readonly IObjectProvider<IType> DomainLayer =
        Types().That().ResideInAssembly(typeof(DomainAssemblyMarker).Assembly).As("Ledger Domain");

    private static readonly IObjectProvider<IType> ApplicationLayer =
        Types().That().ResideInAssembly(typeof(ApplicationAssemblyMarker).Assembly).As("Ledger Application");

    private static readonly IObjectProvider<IType> InfrastructureLayer =
        Types().That().ResideInAssembly(typeof(InfrastructureAssemblyMarker).Assembly).As("Ledger Infrastructure");

    private static readonly IObjectProvider<IType> ApiLayer =
        Types().That().ResideInAssembly(typeof(Program).Assembly).As("Ledger Api");

    // Namespace patterns match the root namespace and every sub-namespace (e.g. Microsoft.AspNetCore.Http).
    private static readonly IObjectProvider<IType> EntityFrameworkCore =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Microsoft\.EntityFrameworkCore(\..+)?$").As("EF Core");

    private static readonly IObjectProvider<IType> AspNetCore =
        Types(includeReferenced: true).That().ResideInNamespaceMatching(@"^Microsoft\.AspNetCore(\..+)?$").As("ASP.NET Core");

    [Fact]
    public void Domain_does_not_depend_on_application()
    {
        Types().That().Are(DomainLayer).Should().NotDependOnAny(ApplicationLayer)
            .Check(LedgerArchitecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_infrastructure()
    {
        Types().That().Are(DomainLayer).Should().NotDependOnAny(InfrastructureLayer)
            .Check(LedgerArchitecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_api()
    {
        Types().That().Are(DomainLayer).Should().NotDependOnAny(ApiLayer)
            .Check(LedgerArchitecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_entity_framework_core()
    {
        Types().That().Are(DomainLayer).Should().NotDependOnAny(EntityFrameworkCore)
            .Check(LedgerArchitecture);
    }

    [Fact]
    public void Domain_does_not_depend_on_aspnet_core()
    {
        Types().That().Are(DomainLayer).Should().NotDependOnAny(AspNetCore)
            .Check(LedgerArchitecture);
    }

    [Fact]
    public void Application_does_not_depend_on_infrastructure()
    {
        Types().That().Are(ApplicationLayer).Should().NotDependOnAny(InfrastructureLayer)
            .Check(LedgerArchitecture);
    }

    [Fact]
    public void Application_does_not_depend_on_api()
    {
        Types().That().Are(ApplicationLayer).Should().NotDependOnAny(ApiLayer)
            .Check(LedgerArchitecture);
    }

    [Fact]
    public void Application_does_not_depend_on_entity_framework_core()
    {
        Types().That().Are(ApplicationLayer).Should().NotDependOnAny(EntityFrameworkCore)
            .Check(LedgerArchitecture);
    }

    [Fact]
    public void Application_does_not_depend_on_aspnet_core()
    {
        Types().That().Are(ApplicationLayer).Should().NotDependOnAny(AspNetCore)
            .Check(LedgerArchitecture);
    }
}
