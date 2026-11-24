<#
.SYNOPSIS
    Starts AutoCAD MCP Server over STDIO transport.
.DESCRIPTION
    Launches AutoCAD.McpServer.dll or AutoCAD.McpServer.exe in Release mode.
    By default, leaves AUTOCAD_GATEWAY_TOKEN unset so the server discovers the active
    AutoCAD Gateway session descriptor automatically.
#>

[CmdletBinding()]
param(
    [string]$GatewayToken = "",
    [switch]$UseExecutable
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseDir = Join-Path $repoRoot "AutoCAD.McpServer\bin\Release\net10.0"
$dllPath = Join-Path $releaseDir "AutoCAD.McpServer.dll"
$exePath = Join-Path $releaseDir "AutoCAD.McpServer.exe"

if (-not [string]::IsNullOrWhiteSpace($GatewayToken)) {
    $env:AUTOCAD_GATEWAY_TOKEN = $GatewayToken.Trim()
}

if ($UseExecutable -and (Test-Path $exePath)) {
    [Console]::Error.WriteLine("Starting AutoCAD MCP Server via executable ($exePath)...")
    & "$exePath"
} elseif (Test-Path $dllPath) {
    [Console]::Error.WriteLine("Starting AutoCAD MCP Server via dotnet ($dllPath)...")
    dotnet "$dllPath"
} else {
    Write-Error "Compiled binaries not found. Run .\scripts\build.ps1 first."
}
