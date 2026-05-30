using System.Diagnostics;
using System.IO.Compression;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Principal;
using Microsoft.Win32;

var installer = new Installer();
return await installer.RunAsync(args);

internal sealed class Installer
{
    private const string AppName = "IntLimiter";
    private const string ServiceName = "IntLimiter.Service";
    private const string PayloadResourceName = "IntLimiterPayload.zip";

    private readonly string _installDir = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
        AppName);

    public async Task<int> RunAsync(string[] args)
    {
        Console.Title = "IntLimiter Setup";
        Console.OutputEncoding = System.Text.Encoding.UTF8;

        try
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException("IntLimiter setup only supports Windows.");
            }

            if (!IsAdministrator())
            {
                throw new InvalidOperationException("Setup must be run as Administrator.");
            }

            if (args.Any(arg => string.Equals(arg, "--uninstall", StringComparison.OrdinalIgnoreCase)))
            {
                await UninstallAsync();
                return 0;
            }

            await InstallAsync();
            return 0;
        }
        catch (Exception ex)
        {
            WriteError(ex.Message);
            if (ex.InnerException is not null)
            {
                WriteError(ex.InnerException.Message);
            }

            WaitBeforeExit();
            return 1;
        }
    }

    private async Task InstallAsync()
    {
        WriteHeader();
        WriteStep("Installing IntLimiter...");

        var payload = GetPayloadStream();
        var tempDir = Path.Combine(Path.GetTempPath(), "IntLimiterSetup-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            WriteStep("Extracting installer payload...");
            using (payload)
            using (var archive = new ZipArchive(payload, ZipArchiveMode.Read))
            {
                archive.ExtractToDirectory(tempDir, overwriteFiles: true);
            }

            WriteStep("Stopping existing service...");
            RunSc("stop", ServiceName, allowFailure: true);
            await Task.Delay(1200);
            RunSc("delete", ServiceName, allowFailure: true);
            await Task.Delay(1200);

            KillAppProcesses();

            WriteStep("Cleaning previous install folder...");
            CleanInstallDirectory();

            WriteStep($"Copying files to {_installDir}...");
            Directory.CreateDirectory(_installDir);
            CopyDirectory(tempDir, _installDir);

            var serviceExe = Path.Combine(_installDir, "IntLimiter.Service.exe");
            var clientExe = Path.Combine(_installDir, "IntLimiter.exe");
            var setupCopy = Path.Combine(_installDir, "IntLimiterSetup.exe");

            if (!File.Exists(serviceExe))
            {
                throw new FileNotFoundException("Service executable missing from payload.", serviceExe);
            }

            if (!File.Exists(clientExe))
            {
                throw new FileNotFoundException("Client executable missing from payload.", clientExe);
            }

            CopySelf(setupCopy);

            WriteStep("Installing Windows service...");
            RunSc("create", ServiceName, $"binPath= \"{serviceExe}\"", "start= auto", "DisplayName= \"IntLimiter Service\"");
            RunSc("description", ServiceName, "\"IntLimiter traffic shaping service\"");
            RunSc("start", ServiceName, allowFailure: true);

            WriteStep("Preparing shared data folder permissions...");
            PrepareProgramDataDirectory();

            WriteStep("Creating shortcuts...");
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "IntLimiter.lnk"),
                clientExe,
                _installDir);
            CreateShortcut(
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "IntLimiter.lnk"),
                clientExe,
                _installDir);

            WriteStep("Writing uninstall entry...");
            WriteUninstallEntry(setupCopy, clientExe);

            Console.WriteLine();
            WriteSuccess("IntLimiter installed successfully.");
            Console.WriteLine($"Install path: {_installDir}");
            Console.WriteLine("You can start IntLimiter from the desktop shortcut.");
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }

        WaitBeforeExit();
    }

    private void CleanInstallDirectory()
    {
        var currentExe = Environment.ProcessPath ?? "";
        if (currentExe.StartsWith(_installDir, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        TryDeleteDirectory(_installDir);
    }

    private static void PrepareProgramDataDirectory()
    {
        var programData = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
            AppName);
        Directory.CreateDirectory(programData);

        RunTool(
            "icacls.exe",
            $"\"{programData}\" /grant *S-1-5-11:(OI)(CI)RX /grant *S-1-5-32-544:(OI)(CI)F /grant *S-1-5-18:(OI)(CI)F /T",
            allowFailure: true);
    }

    private async Task UninstallAsync()
    {
        WriteHeader();
        WriteStep("Uninstalling IntLimiter...");

        RunSc("stop", ServiceName, allowFailure: true);
        await Task.Delay(1200);
        RunSc("delete", ServiceName, allowFailure: true);
        await Task.Delay(1200);

        KillAppProcesses();

        DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), "IntLimiter.lnk"));
        DeleteShortcut(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms), "IntLimiter.lnk"));
        DeleteUninstallEntry();

        var currentExe = Environment.ProcessPath ?? "";
        if (currentExe.StartsWith(_installDir, StringComparison.OrdinalIgnoreCase))
        {
            WriteStep("Scheduling install folder cleanup...");
            StartHidden("cmd.exe", $"/c timeout /t 2 /nobreak > nul & rmdir /s /q \"{_installDir}\"");
        }
        else
        {
            TryDeleteDirectory(_installDir);
        }

        WriteSuccess("IntLimiter uninstalled.");
        WaitBeforeExit();
    }

    private static Stream GetPayloadStream()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var stream = assembly.GetManifestResourceStream(PayloadResourceName);
        if (stream is null)
        {
            throw new InvalidOperationException(
                "Installer payload is missing. Build the setup with scripts\\build-installer.ps1.");
        }

        return stream;
    }

    private void KillAppProcesses()
    {
        foreach (var processName in new[] { "IntLimiter", "IntLimiter.Service" })
        {
            foreach (var process in Process.GetProcessesByName(processName))
            {
                try
                {
                    var path = process.MainModule?.FileName ?? "";
                    if (path.StartsWith(_installDir, StringComparison.OrdinalIgnoreCase))
                    {
                        WriteStep($"Stopping {process.ProcessName} ({process.Id})...");
                        process.Kill(entireProcessTree: true);
                        process.WaitForExit(5000);
                    }
                }
                catch
                {
                    // Best-effort cleanup; service installation will fail clearly if a file stays locked.
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
    }

    private static void CopyDirectory(string source, string destination)
    {
        foreach (var directory in Directory.EnumerateDirectories(source, "*", SearchOption.AllDirectories))
        {
            Directory.CreateDirectory(directory.Replace(source, destination, StringComparison.OrdinalIgnoreCase));
        }

        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var target = file.Replace(source, destination, StringComparison.OrdinalIgnoreCase);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target, overwrite: true);
        }
    }

    private static void CopySelf(string setupCopy)
    {
        var current = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(current) || !File.Exists(current))
        {
            return;
        }

        File.Copy(current, setupCopy, overwrite: true);
    }

    private static void CreateShortcut(string shortcutPath, string targetPath, string workingDirectory)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(shortcutPath)!);
        var shellType = Type.GetTypeFromProgID("WScript.Shell")
            ?? throw new InvalidOperationException("WScript.Shell COM object is unavailable.");
        dynamic shell = Activator.CreateInstance(shellType)
            ?? throw new InvalidOperationException("Could not create WScript.Shell.");
        dynamic shortcut = shell.CreateShortcut(shortcutPath);
        shortcut.TargetPath = targetPath;
        shortcut.WorkingDirectory = workingDirectory;
        shortcut.Description = AppName;
        shortcut.IconLocation = $"{targetPath},0";
        shortcut.Save();
        Marshal.FinalReleaseComObject(shortcut);
        Marshal.FinalReleaseComObject(shell);
    }

    private static void DeleteShortcut(string shortcutPath)
    {
        if (File.Exists(shortcutPath))
        {
            File.Delete(shortcutPath);
        }
    }

    private void WriteUninstallEntry(string setupExe, string iconExe)
    {
        using var key = Registry.LocalMachine.CreateSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\IntLimiter");
        key.SetValue("DisplayName", "IntLimiter");
        key.SetValue("DisplayVersion", "1.0.0");
        key.SetValue("Publisher", "IntLimiter");
        key.SetValue("InstallLocation", _installDir);
        key.SetValue("DisplayIcon", iconExe);
        key.SetValue("UninstallString", $"\"{setupExe}\" --uninstall");
        key.SetValue("NoModify", 1, RegistryValueKind.DWord);
        key.SetValue("NoRepair", 1, RegistryValueKind.DWord);
    }

    private static void DeleteUninstallEntry()
    {
        Registry.LocalMachine.DeleteSubKeyTree(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\IntLimiter", throwOnMissingSubKey: false);
    }

    private static void RunSc(params string[] args)
    {
        RunSc(args, allowFailure: false);
    }

    private static void RunSc(string arg1, string arg2, bool allowFailure)
    {
        RunSc([arg1, arg2], allowFailure);
    }

    private static void RunSc(string[] args, bool allowFailure)
    {
        var commandLine = string.Join(' ', args);
        RunTool("sc.exe", commandLine, allowFailure);
    }

    private static void RunTool(string fileName, string arguments, bool allowFailure)
    {
        using var process = Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        }) ?? throw new InvalidOperationException("Could not start sc.exe.");

        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0 && !allowFailure)
        {
            throw new InvalidOperationException($"{fileName} {arguments} failed: {output} {error}");
        }
    }

    private static void StartHidden(string fileName, string arguments)
    {
        Process.Start(new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            CreateNoWindow = true,
            WindowStyle = ProcessWindowStyle.Hidden
        });
    }

    private static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch
        {
            // Best effort.
        }
    }

    private static void WriteHeader()
    {
        Console.WriteLine("IntLimiter Setup");
        Console.WriteLine("================");
        Console.WriteLine();
    }

    private static void WriteStep(string message)
    {
        Console.ForegroundColor = ConsoleColor.Cyan;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void WriteSuccess(string message)
    {
        Console.ForegroundColor = ConsoleColor.Green;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void WriteError(string message)
    {
        Console.ForegroundColor = ConsoleColor.Red;
        Console.WriteLine(message);
        Console.ResetColor();
    }

    private static void WaitBeforeExit()
    {
        if (Environment.UserInteractive)
        {
            Console.WriteLine();
            Console.WriteLine("Press Enter to close setup.");
            Console.ReadLine();
        }
    }
}
