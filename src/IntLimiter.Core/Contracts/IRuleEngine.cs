using IntLimiter.Core.Models;
using System;
using System.Collections.Generic;

namespace IntLimiter.Core.Contracts;

public interface IRuleEngine
{
    void AddRule(Rule rule);
    void RemoveRule(string ruleId);
    void EnableRule(string ruleId, bool enable);
    IEnumerable<Rule> GetRules();
    void ApplyAllRules();
}

public class Rule
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public int? ProcessId { get; set; } // Null implies Global Rule
    public string? ProcessName { get; set; }
    public long DownloadLimitBytesPerSecond { get; set; }
    public long UploadLimitBytesPerSecond { get; set; }
    public bool IsEnabled { get; set; } = true;
}
