using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;
using ReflectionAssembly = System.Reflection.Assembly;

namespace CardPlatform.Architecture.Tests;

/// <summary>
/// The four layers of a service that follows ADR 0004 (<c>*.Domain</c>, <c>*.Application</c>,
/// <c>*.Infrastructure</c> and the host), each located through its assembly marker. The host is the entry point
/// and composition root of the service: <c>*.Api</c> with the endpoints of the HTTP services, <c>*.Worker</c> for
/// the Fraud Worker, which wires the Infrastructure's message consumers. It is named after the last segment of its
/// assembly name (e.g. "Ledger Api", "Fraud Worker").
/// </summary>
public sealed class ServiceLayers
{
    public ServiceLayers(
        string serviceName,
        ReflectionAssembly domain,
        ReflectionAssembly application,
        ReflectionAssembly infrastructure,
        ReflectionAssembly host)
    {
        DomainAssembly = domain;
        ApplicationAssembly = application;
        Architecture = new ArchLoader()
            .LoadAssemblies(domain, application, infrastructure, host)
            .Build();

        DomainLayer = Types().That().ResideInAssembly(domain).As($"{serviceName} Domain");
        ApplicationLayer = Types().That().ResideInAssembly(application).As($"{serviceName} Application");
        InfrastructureLayer = Types().That().ResideInAssembly(infrastructure).As($"{serviceName} Infrastructure");
        string hostName = host.GetName().Name?.Split('.')[^1] ?? "Host";
        HostLayer = Types().That().ResideInAssembly(host).As($"{serviceName} {hostName}");
    }

    public ArchUnitArchitecture Architecture { get; }

    public ReflectionAssembly DomainAssembly { get; }

    public ReflectionAssembly ApplicationAssembly { get; }

    public IObjectProvider<IType> DomainLayer { get; }

    public IObjectProvider<IType> ApplicationLayer { get; }

    public IObjectProvider<IType> InfrastructureLayer { get; }

    /// <summary>The entry point of the service (its <c>*.Api</c> or <c>*.Worker</c>).</summary>
    public IObjectProvider<IType> HostLayer { get; }
}
