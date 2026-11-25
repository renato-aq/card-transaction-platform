using CardPlatform.Ledger.Api;
using CardPlatform.Ledger.Application;
using CardPlatform.Ledger.Domain;
using CardPlatform.Ledger.Infrastructure;

namespace CardPlatform.Architecture.Tests;

/// <summary>
/// Enforces the dependency rule of ADR 0004 (hexagonal architecture) for the Ledger service.
/// </summary>
public sealed class LedgerArchitectureTests() : HexagonalArchitectureTests(Layers)
{
    private static readonly ServiceLayers Layers = new(
        "Ledger",
        typeof(DomainAssemblyMarker).Assembly,
        typeof(ApplicationAssemblyMarker).Assembly,
        typeof(InfrastructureAssemblyMarker).Assembly,
        typeof(ApiAssemblyMarker).Assembly);
}
