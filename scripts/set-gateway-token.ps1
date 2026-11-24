[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Token,

    [switch]$Reveal
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = "luiccian-" + [System.Security.Cryptography.RandomNumberGenerator]::GetHexString(32, $true)
    Write-Host ""
    Write-Host "[+] Generated new cryptographically random token..." -ForegroundColor Cyan
} else {
    $Token = $Token.Trim()
    Write-Host ""
    Write-Host "[+] Applying user-supplied token..." -ForegroundColor Cyan
}

# Persist token in Windows user environment variables
[System.Environment]::SetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN", $Token, [System.EnvironmentVariableTarget]::User)
$env:AUTOCAD_GATEWAY_TOKEN = $Token

$masked = if ($Reveal) {
    $Token
} elseif ($Token.Length -le 12) {
    "***"
} else {
    $Token.Substring(0, 8) + "..." + $Token.Substring($Token.Length - 4)
}

Write-Host "=======================================================" -ForegroundColor Green
Write-Host "  AUTOCAD AI GATEWAY - TOKEN CONFIGURED" -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Green
Write-Host "Windows User Env Var : AUTOCAD_GATEWAY_TOKEN" -ForegroundColor White
Write-Host "Configured Token     : $masked (pass -Reveal to display full value)" -ForegroundColor Yellow
Write-Host ""
Write-Host "Note: Leaving AUTOCAD_GATEWAY_TOKEN unset is recommended on single-user" -ForegroundColor Gray
Write-Host "workstations so AutoCAD.Gateway auto-mints ephemeral per-session tokens." -ForegroundColor Gray
Write-Host ""
