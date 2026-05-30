$ErrorActionPreference = "Stop"

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Bu script Administrator PowerShell ile calistirilmalidir."
    }
}

Assert-Admin

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot
try {
    dotnet build .\IntLimiter.sln

    $serviceArgs = @("run", "--project", ".\src\IntLimiter.Service\IntLimiter.Service.csproj", "--no-build")
    $service = Start-Process -FilePath "dotnet" -ArgumentList $serviceArgs -WorkingDirectory $repoRoot -WindowStyle Minimized -PassThru
    Start-Sleep -Seconds 2

    Start-Process -FilePath "dotnet" -ArgumentList @("run", "--project", ".\src\IntLimiter.Client\IntLimiter.Client.csproj", "--no-build") -WorkingDirectory $repoRoot -Wait

    if (-not $service.HasExited) {
        Stop-Process -Id $service.Id -Force
    }
}
finally {
    Pop-Location
}
