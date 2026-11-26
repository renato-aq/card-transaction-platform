using CardPlatform.Fraud.Application.Abstractions.Messaging;
using CardPlatform.Fraud.Application.Purchases;

using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Fraud.Application;

public static class DependencyInjection
{
    /// <summary>
    /// Registers the use cases of the Fraud Worker: recording each Authorization decision in the card's purchase history,
    /// and judging each approved purchase by the fraud rules. They need an
    /// <see cref="Abstractions.Persistence.ICardPurchaseRepository"/>, an <see cref="Abstractions.Persistence.IOutbox"/>
    /// and an <see cref="Abstractions.Persistence.IUnitOfWork"/>, registered by the Infrastructure layer, the
    /// <see cref="Domain.Rules.FraudRules"/> with their configured limits and a <see cref="TimeProvider"/>.
    /// </summary>
    public static IServiceCollection AddFraudApplication(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<ICommandHandler<RecordApprovedPurchase, ApprovedPurchaseRecording>, RecordApprovedPurchaseHandler>();
        services.AddScoped<ICommandHandler<RecordDeclinedPurchase, RecordingOutcome>, RecordDeclinedPurchaseHandler>();
        services.AddScoped<ICommandHandler<RecordCapturedPurchase, RecordingOutcome>, RecordCapturedPurchaseHandler>();
        services.AddScoped<ICommandHandler<RecordVoidedPurchase, RecordingOutcome>, RecordVoidedPurchaseHandler>();

        return services;
    }
}
