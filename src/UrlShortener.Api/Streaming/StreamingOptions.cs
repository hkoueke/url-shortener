namespace UrlShortener.Api.Streaming;

/// <summary>Streaming transport options used for MassTransit bus configuration.</summary>
public sealed class StreamingOptions
{
    /// <summary>Gets or sets transport provider: InMemory or RabbitMq.</summary>
    public string Provider { get; set; } = "InMemory";
    /// <summary>Gets or sets RabbitMQ host (for RabbitMq provider).</summary>
    public string? RabbitMqHost { get; set; }
    /// <summary>Gets or sets RabbitMQ virtual host.</summary>
    public string RabbitMqVirtualHost { get; set; } = "/";
    /// <summary>Gets or sets RabbitMQ username.</summary>
    public string? RabbitMqUsername { get; set; }
    /// <summary>Gets or sets RabbitMQ password.</summary>
    public string? RabbitMqPassword { get; set; }
}
