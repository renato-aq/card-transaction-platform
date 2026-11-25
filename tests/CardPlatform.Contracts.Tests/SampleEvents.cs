using CardPlatform.Contracts.Authorizations;
using CardPlatform.Contracts.Fraud;

namespace CardPlatform.Contracts.Tests;

internal static class SampleEvents
{
    public static readonly Guid CardId = Guid.Parse("0199a1b2-0000-7000-8000-000000000001");

    private static readonly Guid AuthorizationId = Guid.Parse("0199a1b2-0000-7000-8000-000000000002");

    private static readonly Guid MerchantAccountId = Guid.Parse("0199a1b2-0000-7000-8000-000000000003");

    // Microsecond precision, as the Authorization stores it, with a non-UTC offset to check it survives.
    private static readonly DateTimeOffset CreatedAt = new DateTimeOffset(2026, 11, 25, 10, 30, 15, TimeSpan.FromHours(-3))
        .AddTicks(1234560);

    private static readonly DateTimeOffset CompletedAt = CreatedAt.AddMinutes(5);

    public static AuthorizationApproved Approved { get; } =
        new(AuthorizationId, CardId, MerchantAccountId, 150.75m, "BRL", CreatedAt);

    public static AuthorizationDeclined Declined { get; } =
        new(AuthorizationId, CardId, MerchantAccountId, 150.75m, "BRL", CreatedAt, DeclineReasons.InsufficientFunds);

    public static AuthorizationCaptured Captured { get; } =
        new(AuthorizationId, CardId, MerchantAccountId, 150.75m, "BRL", CreatedAt, CompletedAt);

    public static AuthorizationVoided Voided { get; } =
        new(AuthorizationId, CardId, MerchantAccountId, 150.75m, "BRL", CreatedAt, CompletedAt);

    public static FraudSuspected FraudSuspected { get; } =
        new(CardId, AuthorizationId, "velocity", "6 purchases in the last 60 seconds", CompletedAt);

    public static TheoryData<IIntegrationEvent, string> AllWithTypeNames => new()
    {
        { Approved, EventTypes.AuthorizationApproved },
        { Declined, EventTypes.AuthorizationDeclined },
        { Captured, EventTypes.AuthorizationCaptured },
        { Voided, EventTypes.AuthorizationVoided },
        { FraudSuspected, EventTypes.FraudSuspected },
    };
}
