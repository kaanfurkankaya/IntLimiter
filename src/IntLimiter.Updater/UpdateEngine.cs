using System;
using System.Threading.Tasks;
using Velopack;
using Velopack.Sources;

namespace IntLimiter.Updater;

public interface IUpdateEngine
{
    Task<UpdateInfo?> CheckForUpdatesAsync();
    Task DownloadAndApplyUpdateAsync(UpdateInfo updateInfo);
    string CurrentVersion { get; }
}

public class UpdateEngine : IUpdateEngine
{
    private const string GithubRepoUrl = "https://github.com/yourname/IntLimiter";
    
    public string CurrentVersion
    {
        get
        {
            try
            {
                var mgr = new UpdateManager(new GithubSource(GithubRepoUrl, null, false));
                return mgr.IsInstalled ? mgr.CurrentVersion?.ToString() ?? "1.0.0" : "1.0.0 (Dev)";
            }
            catch
            {
                return "1.0.0 (Dev)";
            }
        }
    }

    public async Task<UpdateInfo?> CheckForUpdatesAsync()
    {
        try
        {
            var mgr = new UpdateManager(new GithubSource(GithubRepoUrl, null, false));
            if (!mgr.IsInstalled) return null;

            return await mgr.CheckForUpdatesAsync();
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Update check failed: {ex.Message}");
            return null;
        }
    }

    public async Task DownloadAndApplyUpdateAsync(UpdateInfo updateInfo)
    {
        var mgr = new UpdateManager(new GithubSource(GithubRepoUrl, null, false));
        await mgr.DownloadUpdatesAsync(updateInfo);
        mgr.ApplyUpdatesAndRestart(updateInfo);
    }
}
