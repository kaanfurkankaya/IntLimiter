using IntLimiter.Core.RateLimiting;

namespace IntLimiter.Core.Tests;

public sealed class TokenBucketTests
{
    [Fact]
    public void Reserve_AllowsInitialCapacityImmediately()
    {
        var time = new ManualTimeProvider();
        var bucket = new TokenBucket(1024, timeProvider: time);

        var delay = bucket.Reserve(1024);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    [Fact]
    public void Reserve_ReturnsDelayWhenTokensAreExhausted()
    {
        var time = new ManualTimeProvider();
        var bucket = new TokenBucket(1000, capacityBytes: 1000, timeProvider: time);

        Assert.Equal(TimeSpan.Zero, bucket.Reserve(1000));
        var delay = bucket.Reserve(500);

        Assert.InRange(delay.TotalMilliseconds, 499, 501);
    }

    [Fact]
    public void Reserve_RefillsOverTime()
    {
        var time = new ManualTimeProvider();
        var bucket = new TokenBucket(1000, capacityBytes: 1000, timeProvider: time);

        Assert.Equal(TimeSpan.Zero, bucket.Reserve(1000));
        time.Advance(TimeSpan.FromMilliseconds(250));

        var delay = bucket.Reserve(250);

        Assert.Equal(TimeSpan.Zero, delay);
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private long _timestamp;

        public override long TimestampFrequency => TimeSpan.TicksPerSecond;
        public override long GetTimestamp() => _timestamp;

        public void Advance(TimeSpan timeSpan)
        {
            _timestamp += timeSpan.Ticks;
        }
    }
}
