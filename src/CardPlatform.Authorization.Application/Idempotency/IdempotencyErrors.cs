using CardPlatform.Authorization.Domain.Common;

namespace CardPlatform.Authorization.Application.Idempotency;

/// <summary>Expected failures of the idempotency of <c>POST /authorizations</c> (ADR 0008).</summary>
public static class IdempotencyErrors
{
    public const string KeyRequiredCode = "idempotency_key_required";

    public const string InvalidKeyCode = "invalid_idempotency_key";

    public const string KeyReusedCode = "idempotency_key_reused";

    public const string RequestInProgressCode = "idempotency_request_in_progress";

    public static readonly Error KeyRequired = Error.Validation(
        KeyRequiredCode,
        "An idempotency key is required (Idempotency-Key header).");

    public static readonly Error InvalidKey = Error.Validation(
        InvalidKeyCode,
        $"The idempotency key must have at most {IdempotentAuthorizePurchaseHandler.MaxKeyLength} characters.");

    public static readonly Error KeyReused = Error.BusinessRule(
        KeyReusedCode,
        "The idempotency key was already used with a different request.");

    public static readonly Error RequestInProgress = Error.Conflict(
        RequestInProgressCode,
        "A request with the same idempotency key is still being processed. Try again later.");
}
