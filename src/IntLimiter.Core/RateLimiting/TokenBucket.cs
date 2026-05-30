using IntLimiter.Core.Models;

namespace IntLimiter.Core.RateLimiting;

public sealed class TokenBucket
{
    private readonly object _sync = new();
    private readonly TimeProvider _timeProvider;
    private double _tokens;
    private long _lastTimestamp;

    public TokenBucket(long bytesPerSecond, long? capacityBytes = null, TimeProvider? timeProvider = null)
    {
        if (bytesPerSecond <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(bytesPerSecond));
        }

        BytesPerSecond = bytesPerSecond;
        CapacityBytes = Math.Max(bytesPerSecond, capacityBytes ?? bytesPerSecond);
        _tokens = CapacityBytes;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastTimestamp = _timeProvider.GetTimestamp();
    }

    public long BytesPerSecond { get; }
    public long CapacityBytes { get; }

    public TimeSpan Reserve(long byteCount)
    {
        if (byteCount <= 0)
        {
            return TimeSpan.Zero;
        }

        lock (_sync)
        {
            var now = _timeProvider.GetTimestamp();
            Refill(now);

            if (_tokens >= byteCount)
            {
                _tokens -= byteCount;
                return TimeSpan.Zero;
            }

            var deficit = byteCount - _tokens;
            _tokens = 0;

            var deficitSeconds = deficit / (double)BytesPerSecond;
            var delay = TimeSpan.FromSeconds(deficitSeconds);
            var baseTimestamp = Math.Max(now, _lastTimestamp);
            _lastTimestamp = baseTimestamp + ToTimestampDelta(delay);
            return FromTimestampDelta(_lastTimestamp - now);
        }
    }

    private void Refill(long now)
    {
        if (now <= _lastTimestamp)
        {
            return;
        }

        var elapsed = FromTimestampDelta(now - _lastTimestamp).TotalSeconds;
        _tokens = Math.Min(CapacityBytes, _tokens + elapsed * BytesPerSecond);
        _lastTimestamp = now;
    }

    private long ToTimestampDelta(TimeSpan timeSpan) =>
        (long)(timeSpan.TotalSeconds * _timeProvider.TimestampFrequency);

    private TimeSpan FromTimestampDelta(long delta) =>
        TimeSpan.FromSeconds(delta / (double)_timeProvider.TimestampFrequency);
}

public sealed class RuleTokenBucketSet
{
    private readonly Dictionary<Guid, TokenBucket> _buckets = new();
    private readonly object _sync = new();

    public void Configure(IEnumerable<BandwidthRule> rules)
    {
        lock (_sync)
        {
            var activeRuleIds = rules.Where(rule => rule.Enabled && rule.IsValid).Select(rule => rule.RuleId).ToHashSet();
            foreach (var removed in _buckets.Keys.Where(id => !activeRuleIds.Contains(id)).ToArray())
            {
                _buckets.Remove(removed);
            }

            foreach (var rule in rules.Where(rule => rule.Enabled && rule.IsValid))
            {
                if (!_buckets.TryGetValue(rule.RuleId, out var bucket) || bucket.BytesPerSecond != rule.LimitBytesPerSecond)
                {
                    _buckets[rule.RuleId] = new TokenBucket(rule.LimitBytesPerSecond);
                }
            }
        }
    }

    public TimeSpan Reserve(IEnumerable<BandwidthRule> matchedRules, long byteCount)
    {
        TimeSpan maxDelay = TimeSpan.Zero;

        lock (_sync)
        {
            foreach (var rule in matchedRules)
            {
                if (_buckets.TryGetValue(rule.RuleId, out var bucket))
                {
                    var delay = bucket.Reserve(byteCount);
                    if (delay > maxDelay)
                    {
                        maxDelay = delay;
                    }
                }
            }
        }

        return maxDelay;
    }
}
