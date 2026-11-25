using System.Diagnostics.CodeAnalysis;

namespace CardPlatform.Fraud.Domain.Common;

/// <summary>
/// Describes an expected business failure: a stable <c>snake_case</c> <see cref="Code"/>, a human-readable
/// <see cref="Message"/> and its <see cref="Type"/>. Prefer the factories over the constructor.
/// </summary>
[SuppressMessage(
    "Naming",
    "CA1716:Identifiers should not match keywords",
    Justification = "'Error' is only a Visual Basic keyword; the platform is C#-only.")]
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

    public static Error Conflict(string code, string message)
    {
        return new Error(code, message, ErrorType.Conflict);
    }
}
