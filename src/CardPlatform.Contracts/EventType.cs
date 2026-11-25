namespace CardPlatform.Contracts;

/// <summary>
/// A registered integration event: its stable wire <see cref="Name"/> (independent of the CLR name), the current
/// contract <see cref="Version"/> and the payload <see cref="ClrType"/>.
/// </summary>
public sealed record EventType(string Name, int Version, Type ClrType);
