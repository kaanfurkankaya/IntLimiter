namespace IntLimiter.Core.Infrastructure;

public static class ApplicationPaths
{
    public static string ProgramDataDirectory { get; } =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "IntLimiter");

    public static string RuleStorePath => Path.Combine(ProgramDataDirectory, "rules.json");
    public static string LogPath => Path.Combine(ProgramDataDirectory, "IntLimiter.log.jsonl");

    public static void EnsureProgramData()
    {
        Directory.CreateDirectory(ProgramDataDirectory);
    }
}
