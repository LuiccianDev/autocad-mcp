<#
.SYNOPSIS
    Ejecuta el servidor AutoCAD MCP Server vía STDIO.
.DESCRIPTION
    Lanza el proceso de AutoCAD.McpServer.dll o AutoCAD.McpServer.exe en modo Release.
#>

[CmdletBinding()]
param(
    [string]$GatewayToken = "autocad-gateway-token-2027",
    [switch]$UseExecutable
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$releaseDir = Join-Path $repoRoot "AutoCAD.McpServer\bin\Release\net10.0"
$dllPath = Join-Path $releaseDir "AutoCAD.McpServer.dll"
$exePath = Join-Path $releaseDir "AutoCAD.McpServer.exe"

$env:AUTOCAD_GATEWAY_TOKEN = $GatewayToken

if ($UseExecutable -and (Test-Path $exePath)) {
    Write-Host "Iniciando AutoCAD MCP Server via ejecutable ($exePath)..." -ForegroundColor Cyan
    & "$exePath"
} elseif (Test-Path $dllPath) {
    Write-Host "Iniciando AutoCAD MCP Server via dotnet ($dllPath)..." -ForegroundColor Cyan
    dotnet "$dllPath"
} else {
    Write-Error "No se encontraron los binarios compilados. Ejecuta primero .\scripts\build.ps1"
}
