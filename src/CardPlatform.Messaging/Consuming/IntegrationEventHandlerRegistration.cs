using CardPlatform.Contracts;

using Microsoft.Extensions.DependencyInjection;

namespace CardPlatform.Messaging.Consuming;

/// <summary>
/// Which <see cref="IIntegrationEventHandler{TEvent}"/> handles the events of one wire type, registered as a singleton
/// by <see cref="MessagingHostingExtensions.AddIntegrationEventHandler{TEvent, THandler}"/> so the consumer can dispatch
/// an envelope by its <see cref="EventEnvelope.Type"/> without reflection.
/// </summary>
public sealed class IntegrationEventHandlerRegistration
{
    private readonly Func<IServiceProvider, IIntegrationEvent, EventEnvelope, CancellationToken, Task> _invoke;

    private IntegrationEventHandlerRegistration(
        EventType eventType,
        Func<IServiceProvider, IIntegrationEvent, EventEnvelope, CancellationToken, Task> invoke)
    {
        EventType = eventType;
        _invoke = invoke;
    }

    /// <summary>The event type handled (see <see cref="EventTypes"/>).</summary>
    public EventType EventType { get; }

    /// <summary>The registration of the handler of <typeparamref name="TEvent"/>, which must be a registered event.</summary>
    /// <exception cref="ArgumentException"><typeparamref name="TEvent"/> is not in <see cref="EventTypes"/>.</exception>
    public static IntegrationEventHandlerRegistration For<TEvent>()
        where TEvent : IIntegrationEvent
    {
        return new IntegrationEventHandlerRegistration(
            EventTypes.Of(typeof(TEvent)),
            static (services, integrationEvent, envelope, cancellationToken) =>
                services.GetRequiredService<IIntegrationEventHandler<TEvent>>()
                    .HandleAsync((TEvent)integrationEvent, envelope, cancellationToken));
    }

    /// <summary>Resolves the handler from <paramref name="services"/> (the message's scope) and calls it.</summary>
    public Task InvokeAsync(
        IServiceProvider services,
        IIntegrationEvent integrationEvent,
        EventEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(integrationEvent);
        ArgumentNullException.ThrowIfNull(envelope);

        return _invoke(services, integrationEvent, envelope, cancellationToken);
    }
}
