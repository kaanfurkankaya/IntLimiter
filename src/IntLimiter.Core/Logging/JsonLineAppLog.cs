using System.Collections.Concurrent;
using System.Text.Json;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Infrastructure;
using IntLimiter.Core.Models;

namespace IntLimiter.Core.Logging;

public sealed class JsonLineAppLog : IAppLog
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ConcurrentQueue<LogEntry> _recent = new();
    private readonly string _path;

    public JsonLineAppLog(string? path = null)
    {
        _path = path ?? ApplicationPaths.LogPath;
    }

    public void Information(string source, string message) => Write("Information", source, message);
    public void Warning(string source, string message) => Write("Warning", source, message);
    public void Error(string source, string message) => Write("Error", source, message);
    public void Event(string level, string eventName, string source, string message, IReadOnlyDictionary<string, object?>? data = null) =>
        Write(level, source, message, eventName, data);

    public IReadOnlyList<LogEntry> ReadRecent(int take)
    {
        var memoryEntries = _recent.Reverse().Take(Math.Max(1, take)).Reverse().ToArray();
        if (memoryEntries.Length >= take || !File.Exists(_path))
        {
            return memoryEntries;
        }

        try
        {
            return File.ReadLines(_path)
                .Reverse()
                .Take(Math.Max(1, take))
                .Reverse()
                .Select(line => JsonSerializer.Deserialize<LogEntry>(line, JsonOptions))
                .Where(entry => entry is not null)
                .Cast<LogEntry>()
                .ToArray();
        }
        catch
        {
            return memoryEntries;
        }
    }

    private void Write(
        string level,
        string source,
        string message,
        string? eventName = null,
        IReadOnlyDictionary<string, object?>? data = null)
    {
        var entry = new LogEntry
        {
            Timestamp = DateTimeOffset.UtcNow,
            Level = level,
            Event = eventName ?? NormalizeEventName(message),
            Source = source,
            Message = message,
            Data = data ?? new Dictionary<string, object?>()
        };

        _recent.Enqueue(entry);
        while (_recent.Count > 300)
        {
            _recent.TryDequeue(out _);
        }

        try
        {
            ApplicationPaths.EnsureProgramData();
            File.AppendAllText(_path, JsonSerializer.Serialize(entry, JsonOptions) + Environment.NewLine);
        }
        catch
        {
            // Logging must never crash the limiter path.
        }
    }

    private static string NormalizeEventName(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            return "LogMessage";
        }

        var letters = message
            .Where(char.IsLetterOrDigit)
            .Take(64)
            .ToArray();

        return letters.Length == 0 ? "LogMessage" : new string(letters);
    }
}
