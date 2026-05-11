using MassTransit;

namespace UrlShortener.Api.Streaming;

/// <summary>Publishes audit and analytics events to message bus transport.</summary>
public interface IAuditEventPublisher
{
    /// <summary>Publishes an event payload to the configured bus.</summary>
    Task PublishAsync(string eventType, object payload, CancellationToken cancellationToken = default);
}

/// <summary>MassTransit bus publisher implementation.</summary>
public sealed class MassTransitAuditEventPublisher(IPublishEndpoint publishEndpoint) : IAuditEventPublisher
{
    public Task PublishAsync(string eventType, object payload, CancellationToken cancellationToken = default)
        => publishEndpoint.Publish(new AuditEventMessage(eventType, payload, DateTime.UtcNow), cancellationToken);
}

/// <summary>Audit event message contract.</summary>
/// <param name="EventType">Event type name.</param>
/// <param name="Payload">Serialized payload object.</param>
/// <param name="OccurredAtUtc">UTC timestamp.</param>
public sealed record AuditEventMessage(string EventType, object Payload, DateTime OccurredAtUtc);
