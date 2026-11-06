<#
.SYNOPSIS
    Script de compilación para la solución completa de AutoCAD MCP (Gateway + MCP Server).
.DESCRIPTION
    Compila en modo 'Release' la solución AutoCAD_Integration.slnx ubicada en la raíz.
#>

[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release"
)

$ErrorActionPreference = "Stop"

Write-Host "`n=======================================================" -ForegroundColor Cyan
Write-Host "   Compilando Solución AutoCAD MCP ($Configuration)" -ForegroundColor Cyan
Write-Host "=======================================================`n" -ForegroundColor Cyan

$repoRoot = Split-Path -Parent $PSScriptRoot
$solutionPath = Join-Path $repoRoot "AutoCAD_Integration.slnx"

if (-not (Test-Path $solutionPath)) {
    Write-Error "No se encontro la solucion en: $solutionPath"
}

Write-Host "Ejecutando dotnet build..." -ForegroundColor Yellow
dotnet build "$solutionPath" -c $Configuration

if ($LASTEXITCODE -eq 0) {
    Write-Host "`n[OK] Compilacion completada exitosamente!" -ForegroundColor Green
    Write-Host "  -> Gateway DLL: AutoCAD.Gateway\bin\$Configuration\net10.0-windows\AutoCAD.Gateway.dll" -ForegroundColor DarkGray
    Write-Host "  -> MCP Server:  AutoCAD.McpServer\bin\$Configuration\net10.0\AutoCAD.McpServer.dll" -ForegroundColor DarkGray
} else {
    Write-Host "`n[ERROR] Fallo la compilacion. Codigo de salida: $LASTEXITCODE" -ForegroundColor Red
    Write-Host "Nota: Si el archivo esta bloqueado, recuerda cerrar AutoCAD antes de compilar." -ForegroundColor Yellow
    exit $LASTEXITCODE
}
