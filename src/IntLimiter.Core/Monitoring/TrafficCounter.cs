using System.Collections.Concurrent;
using IntLimiter.Core.Models;

namespace IntLimiter.Core.Monitoring;

public sealed class TrafficCounter
{
    private static readonly TimeSpan RateWindow = TimeSpan.FromMilliseconds(500);
    private readonly ConcurrentDictionary<int, CounterState> _counters = new();

    public void Add(ProcessIdentity process, TrafficDirection direction, long bytes)
    {
        if (process.ProcessId <= 0 || bytes <= 0)
        {
            return;
        }

        var state = _counters.GetOrAdd(process.ProcessId, _ => new CounterState(process));
        state.Add(process, direction, bytes);
    }

    public IReadOnlyList<ProcessIdentity> Snapshot()
    {
        return _counters.Values
            .Select(counter => counter.Snapshot())
            .Where(snapshot => snapshot.UploadBytesPerSecond > 0 || snapshot.DownloadBytesPerSecond > 0)
            .OrderByDescending(snapshot => snapshot.UploadBytesPerSecond + snapshot.DownloadBytesPerSecond)
            .ToArray();
    }

    private sealed class CounterState
    {
        private readonly object _sync = new();
        private ProcessIdentity _process;
        private long _uploadBytes;
        private long _downloadBytes;
        private long _lastUploadRate;
        private long _lastDownloadRate;
        private DateTimeOffset _windowStarted = DateTimeOffset.UtcNow;

        public CounterState(ProcessIdentity process)
        {
            _process = process;
        }

        public void Add(ProcessIdentity process, TrafficDirection direction, long bytes)
        {
            lock (_sync)
            {
                _process = process;
                RollWindowIfNeeded(DateTimeOffset.UtcNow);
                if (direction == TrafficDirection.Upload)
                {
                    _uploadBytes += bytes;
                }
                else if (direction == TrafficDirection.Download)
                {
                    _downloadBytes += bytes;
                }
            }
        }

        public ProcessIdentity Snapshot()
        {
            lock (_sync)
            {
                RollWindowIfNeeded(DateTimeOffset.UtcNow);
                return _process with
                {
                    UploadBytesPerSecond = _lastUploadRate,
                    DownloadBytesPerSecond = _lastDownloadRate
                };
            }
        }

        private void RollWindowIfNeeded(DateTimeOffset now)
        {
            var elapsed = now - _windowStarted;
            if (elapsed < RateWindow)
            {
                return;
            }

            var seconds = Math.Max(0.25, elapsed.TotalSeconds);
            _lastUploadRate = (long)(_uploadBytes / seconds);
            _lastDownloadRate = (long)(_downloadBytes / seconds);
            _uploadBytes = 0;
            _downloadBytes = 0;
            _windowStarted = now;
        }
    }
}
