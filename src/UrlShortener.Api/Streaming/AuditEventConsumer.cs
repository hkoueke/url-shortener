using MassTransit;

namespace UrlShortener.Api.Streaming;

/// <summary>Consumes emitted audit events from the message bus for downstream processing hooks.</summary>
public sealed class AuditEventConsumer(ILogger<AuditEventConsumer> logger) : IConsumer<AuditEventMessage>
{
    public Task Consume(ConsumeContext<AuditEventMessage> context)
    {
        logger.LogInformation("Consumed stream event {EventType} at {OccurredAtUtc}", context.Message.EventType, context.Message.OccurredAtUtc);
        return Task.CompletedTask;
    }
}
