namespace UrlShortener.Api.Core;

/// <summary>Generates globally unique distributed 64-bit identifiers using a Snowflake layout.</summary>
public interface ISnowflakeGenerator
{
    /// <summary>Creates a new Snowflake identifier.</summary>
    /// <returns>A unique 64-bit integer.</returns>
    long NextId();
}

/// <summary>Produces monotonic Snowflake IDs with 41-bit timestamp, 10-bit worker id, and 12-bit sequence.</summary>
public sealed class SnowflakeGenerator : ISnowflakeGenerator
{
    private const int WorkerIdBits = 10;
    private const int SequenceBits = 12;
    private const int MaxWorkerId = (1 << WorkerIdBits) - 1;
    private const int MaxSequence = (1 << SequenceBits) - 1;
    private static readonly DateTimeOffset Epoch = new(2024, 1, 1, 0, 0, 0, TimeSpan.Zero);

    private readonly int _workerId;
    private readonly TimeProvider _timeProvider;
    private readonly object _lock = new();
    private long _lastTimestamp = -1;
    private int _sequence;

    /// <summary>Initializes a new <see cref="SnowflakeGenerator"/>.</summary>
    /// <param name="configuration">The service configuration.</param>
    /// <param name="timeProvider">The application time provider.</param>
    public SnowflakeGenerator(IConfiguration configuration, TimeProvider timeProvider)
    {
        var workerText = Environment.GetEnvironmentVariable("SNOWFLAKE_WORKER_ID") ?? configuration["Snowflake:WorkerId"] ?? "0";
        _workerId = int.Parse(workerText);
        if (_workerId is < 0 or > MaxWorkerId)
        {
            throw new ArgumentOutOfRangeException(nameof(configuration), $"Worker ID must be between 0 and {MaxWorkerId}.");
        }

        _timeProvider = timeProvider;
    }

    /// <inheritdoc />
    public long NextId()
    {
        lock (_lock)
        {
            var timestamp = GetTimestamp();
            if (timestamp < _lastTimestamp)
            {
                throw new InvalidOperationException("Clock moved backwards; refusing to generate duplicate Snowflake IDs.");
            }

            if (timestamp == _lastTimestamp)
            {
                _sequence = (_sequence + 1) & MaxSequence;
                if (_sequence == 0)
                {
                    timestamp = WaitForNextMillisecond(_lastTimestamp);
                }
            }
            else
            {
                _sequence = 0;
            }

            _lastTimestamp = timestamp;
            return (timestamp << (WorkerIdBits + SequenceBits)) | ((long)_workerId << SequenceBits) | (uint)_sequence;
        }
    }

    private long WaitForNextMillisecond(long lastTimestamp)
    {
        var timestamp = GetTimestamp();
        while (timestamp <= lastTimestamp)
        {
            timestamp = GetTimestamp();
        }

        return timestamp;
    }

    private long GetTimestamp() => (long)(_timeProvider.GetUtcNow() - Epoch).TotalMilliseconds;
}
