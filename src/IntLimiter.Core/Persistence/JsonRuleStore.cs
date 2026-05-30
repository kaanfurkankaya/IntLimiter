using System.Text.Json;
using System.Text.Json.Serialization;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Infrastructure;
using IntLimiter.Core.Models;

namespace IntLimiter.Core.Persistence;

public sealed class JsonRuleStore : IRuleStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly string _path;

    public JsonRuleStore(string? path = null)
    {
        _path = path ?? ApplicationPaths.RuleStorePath;
    }

    public async Task<IReadOnlyList<BandwidthRule>> LoadAsync(CancellationToken cancellationToken)
    {
        if (!File.Exists(_path))
        {
            return [];
        }

        await using var stream = File.OpenRead(_path);
        var rules = await JsonSerializer.DeserializeAsync<List<BandwidthRule>>(stream, JsonOptions, cancellationToken);
        return rules?.Where(rule => rule.IsValid).ToArray() ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<BandwidthRule> rules, CancellationToken cancellationToken)
    {
        ApplicationPaths.EnsureProgramData();
        var tempPath = _path + ".tmp";
        await using (var stream = File.Create(tempPath))
        {
            await JsonSerializer.SerializeAsync(stream, rules, JsonOptions, cancellationToken);
        }

        File.Move(tempPath, _path, true);
    }
}
