$ErrorActionPreference = "Stop"

$outputFile = "INTLIMITER_AUDIT_EXPORT.txt"
if (Test-Path $outputFile) { Remove-Item $outputFile }

function Write-Section {
    param([string]$title)
    Add-Content $outputFile ""
    Add-Content $outputFile "=================================================="
    if ($title -eq "INTLIMITER AUDIT EXPORT") {
        Add-Content $outputFile $title
        Add-Content $outputFile "=================================================="
        Add-Content $outputFile ""
    } else {
        Add-Content $outputFile $title
    }
}

function Write-FileContent {
    param([string]$filePath)
    $normalizedPath = $filePath -replace '\\', '/'
    Add-Content $outputFile ""
    Add-Content $outputFile "----- FILE START: $normalizedPath -----"
    if (Test-Path $filePath) {
        $content = Get-Content $filePath -Raw -Encoding UTF8
        # Some files might have no trailing newline, make sure we append cleanly
        if ($content -eq $null) {
            Add-Content $outputFile ""
        } else {
            Add-Content $outputFile $content
        }
    } else {
        Add-Content $outputFile "File does not exist: $normalizedPath"
    }
    Add-Content $outputFile "----- FILE END: $normalizedPath -----"
}

function Write-CommandResult {
    param([string]$cmdText, [string]$scriptBlock)
    
    Add-Content $outputFile ""
    Add-Content $outputFile "----- COMMAND START: $cmdText -----"
    Write-Host "Running: $cmdText"
    $output = Invoke-Expression $scriptBlock 2>&1
    
    foreach ($line in $output) {
        Add-Content $outputFile "$line"
    }
    
    $success = $?
    $statusText = if ($success) { "SUCCESS" } else { "FAILED" }
    Add-Content $outputFile ""
    Add-Content $outputFile "[RESULT: $statusText]"
    Add-Content $outputFile "----- COMMAND END: $cmdText -----"
}

Add-Content $outputFile "=================================================="
Add-Content $outputFile "INTLIMITER AUDIT EXPORT"
Add-Content $outputFile "=================================================="
Add-Content $outputFile ""

Add-Content $outputFile "[SECTION 1] FULL REPOSITORY TREE"
$treeOutput = tree /F /A
foreach ($line in $treeOutput) {
    Add-Content $outputFile $line
}


Write-Section "[SECTION 2] SOLUTION AND PROJECT FILES"
$sec2Files = @(
    "IntLimiter.sln",
    "src\IntLimiter.App\IntLimiter.App.csproj",
    "src\IntLimiter.Core\IntLimiter.Core.csproj",
    "src\IntLimiter.Infrastructure\IntLimiter.Infrastructure.csproj",
    "src\IntLimiter.Monitoring\IntLimiter.Monitoring.csproj",
    "src\IntLimiter.RateLimiting\IntLimiter.RateLimiting.csproj",
    "src\IntLimiter.Service\IntLimiter.Service.csproj",
    "src\IntLimiter.UI\IntLimiter.UI.csproj",
    "src\IntLimiter.Updater\IntLimiter.Updater.csproj",
    "tests\IntLimiter.Core.Tests\IntLimiter.Core.Tests.csproj"
)
foreach ($f in $sec2Files) { Write-FileContent $f }

Write-Section "[SECTION 3] APP BOOTSTRAP AND UI FILES"
$sec3Files = @(
    "src\IntLimiter.App\App.xaml",
    "src\IntLimiter.App\App.xaml.cs",
    "src\IntLimiter.App\MainWindow.xaml",
    "src\IntLimiter.App\MainWindow.xaml.cs",
    "src\IntLimiter.App\Pages\DashboardPage.xaml",
    "src\IntLimiter.App\Pages\DashboardPage.xaml.cs",
    "src\IntLimiter.App\Pages\ProcessesPage.xaml",
    "src\IntLimiter.App\Pages\ProcessesPage.xaml.cs",
    "src\IntLimiter.App\Pages\LimitsPage.xaml",
    "src\IntLimiter.App\Pages\LimitsPage.xaml.cs",
    "src\IntLimiter.App\Pages\SettingsPage.xaml",
    "src\IntLimiter.App\Pages\SettingsPage.xaml.cs"
)
foreach ($f in $sec3Files) { Write-FileContent $f }

Write-Section "[SECTION 4] VIEWMODELS"
$sec4Files = @(
    "src\IntLimiter.UI\ViewModels\DashboardViewModel.cs",
    "src\IntLimiter.UI\ViewModels\ProcessesViewModel.cs",
    "src\IntLimiter.UI\ViewModels\LimitsViewModel.cs",
    "src\IntLimiter.UI\ViewModels\SettingsViewModel.cs"
)
foreach ($f in $sec4Files) { Write-FileContent $f }

Write-Section "[SECTION 5] CORE LOGIC"
$sec5Files = @(
    "src\Contracts\IRuleEngine.cs",
    "src\Contracts\ITrafficMonitor.cs",
    "src\Models\NetworkUnits.cs",
    "src\Models\ProcessNetworkUsage.cs",
    "src\IntLimiter.Monitoring\TrafficMonitor.cs",
    "src\IntLimiter.RateLimiting\RuleEngine.cs",
    "src\IntLimiter.Infrastructure\Stores\RuleStore.cs",
    "src\IntLimiter.Updater\UpdateEngine.cs"
)
foreach ($f in $sec5Files) { Write-FileContent $f }

Write-Section "[SECTION 6] SERVICE LAYER"
$sec6Files = @(
    "src\IntLimiter.Service\Program.cs",
    "src\IntLimiter.Service\Worker.cs",
    "src\IntLimiter.Service\TrafficControllerWorker.cs",
    "src\IntLimiter.Service\appsettings.json"
)
foreach ($f in $sec6Files) { Write-FileContent $f }

Write-Section "[SECTION 7] TEST FILES"
$sec7Files = @(
    "tests\IntLimiter.Core.Tests\RuleEngineTests.cs",
    "tests\IntLimiter.Core.Tests\UnitConversionTests.cs"
)
foreach ($f in $sec7Files) { Write-FileContent $f }

Write-Section "[SECTION 8] BUILD / PACKAGE SCRIPTS"
$sec8Files = @(
    "build_release.ps1"
)
foreach ($f in $sec8Files) { Write-FileContent $f }

Write-Section "[SECTION 9] BUILD AND RUN COMMANDS"
$cmds = @"
- restore: dotnet restore IntLimiter.sln
- build: dotnet build IntLimiter.sln
- run app: dotnet run --project src\IntLimiter.App\IntLimiter.App.csproj
- run tests: dotnet test IntLimiter.sln
- package release: powershell -ExecutionPolicy Bypass -File .\build_release.ps1
"@
Add-Content $outputFile $cmds

Write-Section "[SECTION 10] ACTUAL CURRENT CAPABILITIES"
$caps = @"
Fully working now:
- The WinUI 3 Desktop App layout (Dashboard, Processes, Limits, Settings pages)
- The MVVM data-binding via CommunityToolkit.Mvvm
- Reading Rules from JSON via RuleStore.cs
- Running Unit Tests for Rule logic and mathematical Unit Conversions
- Generating Velopack releases via the build script

Works only when run as Administrator:
- The `TrafficMonitor` (Microsoft.Diagnostics.Tracing.TraceEvent ETW session) requires elevated privileges to listen to OS-level network providers. If run as non-admin, the ETW setup fails or is denied.

UI only / simulated:
- Process data flow to the UI exists structurally, but real per-process drop/throttle is simulated at the RuleEngine.cs boundary.
- Velopack update checker checks for hardcoded paths / relies on you having a real URL configured.

Placeholder / not implemented:
- Named Pipes serialization / IPC to background service.
- The actual WFP kernel driver (.sys) network drop mechanism.
- The Windows Service installation commands (IntLimiter.Service right now is just a .NET background worker template).

Experimental / incomplete:
- The `IntLimiter.Service` project is an incomplete placeholder awaiting implementation to bridge UI configuration to the WFP layer.
"@
Add-Content $outputFile $caps

Write-Section "[SECTION 11] PLACEHOLDER / NONFUNCTIONAL / MANUAL FIXES"
$fixes = @"
- src\IntLimiter.Service\Worker.cs (placeholder)
- src\IntLimiter.Service\TrafficControllerWorker.cs (placeholder)
- src\IntLimiter.RateLimiting\RuleEngine.cs -> ApplyAllRules() (stub, requires IPC and WFP driver)
- The entire system requires a WFP Callout Driver (C/C++) written externally to do the actual network limiting.
- An EV Code Signing Certificate is required for the driver.
- The Background Service must be manually installed using `sc create` or similar logic inside an MSIX/InnoSetup later, as `build_release.ps1` only packs the App frontend right now.
"@
Add-Content $outputFile $fixes


Write-Section "[SECTION 12] BUILD VERIFICATION"

$ErrorActionPreference = "Continue" # So commands can fail and we still run the rest

Write-CommandResult "dotnet restore IntLimiter.sln" "dotnet restore IntLimiter.sln"
Write-CommandResult "dotnet build IntLimiter.sln" "dotnet build IntLimiter.sln"
Write-CommandResult "dotnet test IntLimiter.sln" "dotnet test IntLimiter.sln"
Write-CommandResult "powershell -ExecutionPolicy Bypass -File .\build_release.ps1" "powershell -ExecutionPolicy Bypass -File .\build_release.ps1"


Write-Section "[SECTION 13] FINAL HONEST STATUS"
$finalStatus = @"

1. FULLY WORKING NOW
- WinUI 3 Frontend Shell, Pages, and ViewModels
- ETW Tracking (Admin-only capability to observe traffic)
- Local Rule Saving & Reading into JSON
- Local Release Script using Velopack
- Unit testing for algorithms 

2. REQUIRES ADMIN RIGHTS
- ETW Network Provider access
- Registering Windows Services
- Future WFP driver configuration

3. REQUIRES PRODUCTION SIGNING / SPECIAL DEPLOYMENT
- WFP Network Limiter Kernel Driver (.sys) must be signed with EV hardware token to run on modern Windows 11 without Test Mode.
- Velopack automated updater needs an actual production HTTP URL (S3 / GitHub Pages) and signed assemblies.

4. EXACT NEXT STEPS TO SHIP V1
- Step 1: Write a WFP C/C++ driver that accepts Process IDs and bandwidth caps.
- Step 2: Write IntLimiter.Service IPC (Named Pipes) to communicate with the driver.
- Step 3: Implement Named Pipe client in RuleEngine.cs to send the JSON rules over.
- Step 4: Buy EV code signing cert, sign the WFP driver, and build an installer that elevates once to install the driver and service, allowing the WinUI 3 app to run as standard user thereafter.
"@
Add-Content $outputFile $finalStatus

Write-Host "Export fully generated to $PWD\$outputFile"
