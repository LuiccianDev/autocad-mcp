<#
.SYNOPSIS
    Builds and validates python distribution packages (sdist and wheel) for PyPI.
#>
[CmdletBinding()]
param()

$ErrorActionPreference = "Stop"

$ProjectDir = Join-Path $PSScriptRoot "..\AutoCAD.McpPython"
$DistDir = Join-Path $ProjectDir "dist"

Write-Host "==> Cleaning old distribution artifacts..." -ForegroundColor Cyan
if (Test-Path $DistDir) {
    Remove-Item -Path $DistDir -Recurse -Force
}

Write-Host "==> Building package with uv..." -ForegroundColor Cyan
uv build --directory $ProjectDir

Write-Host "==> Running twine check on generated artifacts..." -ForegroundColor Cyan
uv run --directory $ProjectDir twine check "$DistDir/*"

Write-Host "==> Package build and validation completed successfully!" -ForegroundColor Green
