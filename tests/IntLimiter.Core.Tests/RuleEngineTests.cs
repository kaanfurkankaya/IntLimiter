using IntLimiter.Core.Contracts;
using IntLimiter.RateLimiting;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace IntLimiter.Core.Tests;

public class RuleEngineTests
{
    [Fact]
    public void AddRule_IncreasesRuleCount()
    {
        var rules = new List<Rule>();
        var engine = new RuleEngine(rules, r => { });

        engine.AddRule(new Rule { Name = "Test" });

        Assert.Single(engine.GetRules());
        Assert.Equal("Test", engine.GetRules().First().Name);
    }

    [Fact]
    public void RemoveRule_DecreasesRuleCount()
    {
        var rule = new Rule { Name = "Test" };
        var rules = new List<Rule> { rule };
        var engine = new RuleEngine(rules, r => { });

        engine.RemoveRule(rule.Id);

        Assert.Empty(engine.GetRules());
    }
}
