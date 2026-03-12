using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using IntLimiter.Core.Contracts;
using IntLimiter.Core.Models;
using System.Collections.ObjectModel;
using System.Linq;

namespace IntLimiter.UI.ViewModels;

public partial class LimitsViewModel : ObservableObject
{
    private readonly IRuleEngine _ruleEngine;

    public ObservableCollection<Rule> Rules { get; } = new();

    public LimitsViewModel(IRuleEngine ruleEngine)
    {
        _ruleEngine = ruleEngine;
        LoadRules();
    }

    private void LoadRules()
    {
        Rules.Clear();
        foreach (var rule in _ruleEngine.GetRules())
        {
            Rules.Add(rule);
        }
    }

    [RelayCommand]
    private void AddMockRule()
    {
        var rule = new Rule
        {
            Name = "Global Output Limit",
            DownloadLimitBytesPerSecond = 5 * 1024 * 1024,
            UploadLimitBytesPerSecond = 1 * 1024 * 1024,
        };
        _ruleEngine.AddRule(rule);
        LoadRules();
    }
    
    [RelayCommand]
    private void RemoveRule(string id)
    {
        _ruleEngine.RemoveRule(id);
        LoadRules();
    }
}
