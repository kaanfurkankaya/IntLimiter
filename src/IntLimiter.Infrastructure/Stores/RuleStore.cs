using IntLimiter.Core.Models;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace IntLimiter.Infrastructure.Stores;

public class RuleStore
{
    private readonly string _filePath;

    public RuleStore()
    {
        var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IntLimiter");
        Directory.CreateDirectory(folder);
        _filePath = Path.Combine(folder, "rules.json");
    }

    public List<Rule> Load()
    {
        if (!File.Exists(_filePath)) return new List<Rule>();
        
        try
        {
            var json = File.ReadAllText(_filePath);
            return JsonSerializer.Deserialize<List<Rule>>(json) ?? new List<Rule>();
        }
        catch
        {
            return new List<Rule>();
        }
    }

    public void Save(List<Rule> rules)
    {
        var json = JsonSerializer.Serialize(rules, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(_filePath, json);
    }
}
