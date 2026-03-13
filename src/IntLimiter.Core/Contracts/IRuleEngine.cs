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

