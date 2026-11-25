using System.Diagnostics.CodeAnalysis;

namespace CardPlatform.Ledger.Domain.Common;

/// <summary>
/// Describes an expected business failure. <see cref="Code"/> is a stable <c>snake_case</c> identifier
/// (exposed by the API); <see cref="Message"/> is a human-readable explanation.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "'Error' is only a Visual Basic keyword; the platform is C#-only and the spec names this type Error.")]
public sealed record Error
{
    public Error(string code, string message)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        Code = code;
        Message = message;
    }

    public string Code { get; }

    public string Message { get; }
}
