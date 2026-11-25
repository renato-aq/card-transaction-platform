using System.Diagnostics.CodeAnalysis;

namespace CardPlatform.Authorization.Domain.Common;

/// <summary>
/// Describes an expected business failure. <see cref="Code"/> is a stable <c>snake_case</c> identifier
/// (exposed by the API); <see cref="Message"/> is a human-readable explanation; <see cref="Type"/> classifies
/// the failure. Prefer the factories (<see cref="Validation"/>, <see cref="NotFound"/>, <see cref="BusinessRule"/>,
/// <see cref="Conflict"/>, <see cref="Unavailable"/>) over the constructor.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "'Error' is only a Visual Basic keyword; the platform is C#-only and the spec names this type Error.")]
public sealed record Error
{
    public Error(string code, string message, ErrorType type)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(message);

        if (!Enum.IsDefined(type))
        {
            throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown error type.");
        }

        Code = code;
        Message = message;
        Type = type;
    }

    public string Code { get; }

    public string Message { get; }

    public ErrorType Type { get; }

    public static Error Validation(string code, string message)
    {
        return new Error(code, message, ErrorType.Validation);
    }

    public static Error NotFound(string code, string message)
    {
        return new Error(code, message, ErrorType.NotFound);
    }

    public static Error BusinessRule(string code, string message)
    {
        return new Error(code, message, ErrorType.BusinessRule);
    }

    public static Error Conflict(string code, string message)
    {
        return new Error(code, message, ErrorType.Conflict);
    }

    public static Error Unavailable(string code, string message)
    {
        return new Error(code, message, ErrorType.Unavailable);
    }
}
