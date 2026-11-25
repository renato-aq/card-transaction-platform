using CardPlatform.Authorization.Api;
using CardPlatform.Authorization.Application;
using CardPlatform.Authorization.Domain;
using CardPlatform.Authorization.Infrastructure;

namespace CardPlatform.Architecture.Tests;

/// <summary>
/// Enforces the dependency rule of ADR 0004 (hexagonal architecture) for the Authorization service.
/// </summary>
public sealed class AuthorizationArchitectureTests() : HexagonalArchitectureTests(Layers)
{
    private static readonly ServiceLayers Layers = new(
        "Authorization",
        typeof(DomainAssemblyMarker).Assembly,
        typeof(ApplicationAssemblyMarker).Assembly,
        typeof(InfrastructureAssemblyMarker).Assembly,
        typeof(ApiAssemblyMarker).Assembly);
}
