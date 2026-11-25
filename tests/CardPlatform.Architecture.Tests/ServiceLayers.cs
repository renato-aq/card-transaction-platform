using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.Loader;

using static ArchUnitNET.Fluent.ArchRuleDefinition;

using ArchUnitArchitecture = ArchUnitNET.Domain.Architecture;
using ReflectionAssembly = System.Reflection.Assembly;

namespace CardPlatform.Architecture.Tests;

/// <summary>
/// The four layers of a service that follows ADR 0004 (<c>*.Domain</c>, <c>*.Application</c>,
/// <c>*.Infrastructure</c>, <c>*.Api</c>), each located through its assembly marker.
/// </summary>
public sealed class ServiceLayers
{
    public ServiceLayers(
        string serviceName,
        ReflectionAssembly domain,
        ReflectionAssembly application,
        ReflectionAssembly infrastructure,
        ReflectionAssembly api)
    {
        DomainAssembly = domain;
        ApplicationAssembly = application;
        Architecture = new ArchLoader()
            .LoadAssemblies(domain, application, infrastructure, api)
            .Build();

        DomainLayer = Types().That().ResideInAssembly(domain).As($"{serviceName} Domain");
        ApplicationLayer = Types().That().ResideInAssembly(application).As($"{serviceName} Application");
        InfrastructureLayer = Types().That().ResideInAssembly(infrastructure).As($"{serviceName} Infrastructure");
        ApiLayer = Types().That().ResideInAssembly(api).As($"{serviceName} Api");
    }

    public ArchUnitArchitecture Architecture { get; }

    public ReflectionAssembly DomainAssembly { get; }

    public ReflectionAssembly ApplicationAssembly { get; }

    public IObjectProvider<IType> DomainLayer { get; }

    public IObjectProvider<IType> ApplicationLayer { get; }

    public IObjectProvider<IType> InfrastructureLayer { get; }

    public IObjectProvider<IType> ApiLayer { get; }
}
