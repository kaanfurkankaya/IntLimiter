using IntLimiter.Core.Contracts;
using IntLimiter.Core.Models;
using System;
using System.Collections.Generic;
using System.Linq;

namespace IntLimiter.RateLimiting;

public class RuleEngine : IRuleEngine
{
    private readonly List<Rule> _rules = new();
    private readonly Action<List<Rule>> _saveRulesCallback;

    public RuleEngine(IEnumerable<Rule> initialRules, Action<List<Rule>> saveCallback)
    {
        _rules.AddRange(initialRules);
        _saveRulesCallback = saveCallback;
    }

    public void AddRule(Rule rule)
    {
        _rules.Add(rule);
        _saveRulesCallback(_rules);
        ApplyAllRules();
    }

    public void RemoveRule(string ruleId)
    {
        _rules.RemoveAll(r => r.Id == ruleId);
        _saveRulesCallback(_rules);
        ApplyAllRules();
    }

    public void EnableRule(string ruleId, bool enable)
    {
        var rule = _rules.FirstOrDefault(r => r.Id == ruleId);
        if (rule != null)
        {
            rule.IsEnabled = enable;
            _saveRulesCallback(_rules);
            ApplyAllRules();
        }
    }

    public IEnumerable<Rule> GetRules()
    {
        return _rules.ToList();
    }

    public void ApplyAllRules()
    {
        // Network throttling architecture boundary
        // In a real deployment, we open a Named Pipe or gRPC stream to IntLimiter.Service here:
        System.Diagnostics.Debug.WriteLine($"[RuleEngine] Forwarding {_rules.Count(r => r.IsEnabled)} enabled rules to the IntLimiter.Service proxy...");
        System.Diagnostics.Debug.WriteLine("[RuleEngine] Awaiting signed WFP kernel driver for active traffic interception.");
    }
}
