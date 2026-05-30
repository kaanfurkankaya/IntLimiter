$ErrorActionPreference = "Stop"

function Assert-Admin {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Bu script Administrator PowerShell ile calistirilmalidir."
    }
}

Assert-Admin

if (Get-Service -Name "IntLimiter.Service" -ErrorAction SilentlyContinue) {
    Stop-Service -Name "IntLimiter.Service" -ErrorAction SilentlyContinue
    sc.exe delete "IntLimiter.Service" | Out-Null
}

Get-NetQosPolicy -PolicyStore ActiveStore -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -like "IntLimiter_*" } |
    ForEach-Object { Remove-NetQosPolicy -Name $_.Name -PolicyStore ActiveStore -Confirm:$false -ErrorAction SilentlyContinue }

Write-Host "IntLimiter service ve IntLimiter_* QoS policy'leri temizlendi."
