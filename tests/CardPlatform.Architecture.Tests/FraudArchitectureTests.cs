using CardPlatform.Fraud.Application;
using CardPlatform.Fraud.Domain;
using CardPlatform.Fraud.Infrastructure;
using CardPlatform.Fraud.Worker;

namespace CardPlatform.Architecture.Tests;

/// <summary>
/// Enforces the dependency rule of ADR 0004 (hexagonal architecture) for the Fraud Worker, whose host is a worker
/// instead of an HTTP API.
/// </summary>
public sealed class FraudArchitectureTests() : HexagonalArchitectureTests(Layers)
{
    private static readonly ServiceLayers Layers = new(
        "Fraud",
        typeof(DomainAssemblyMarker).Assembly,
        typeof(ApplicationAssemblyMarker).Assembly,
        typeof(InfrastructureAssemblyMarker).Assembly,
        typeof(WorkerAssemblyMarker).Assembly);
}
