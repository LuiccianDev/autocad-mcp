<#
.SYNOPSIS
    Synchronizes release versions across the autocad-mcp manifests and fallback strings.
.EXAMPLE
    .\scripts\bump-version.ps1 -NewVersion "2.5.0"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$NewVersion
)

$ErrorActionPreference = "Stop"

$cleanVersion = $NewVersion.TrimStart('v').Trim()
if ($cleanVersion -notmatch '^\d+\.\d+\.\d+([-.][0-9A-Za-z.]+)?$') {
    throw "Invalid SemVer version '$cleanVersion'."
}

Write-Host ">>> Synchronizing ecosystem version: $cleanVersion" -ForegroundColor Cyan

$pyprojectPath = "AutoCAD.McpPython\pyproject.toml"
if (Test-Path $pyprojectPath) {
    (Get-Content $pyprojectPath -Raw) -replace 'version\s*=\s*"[^"]+"', "version = `"$cleanVersion`"" | Set-Content $pyprojectPath -NoNewline
    Write-Host "  [OK] $pyprojectPath -> $cleanVersion" -ForegroundColor Green
}

$initPath = "AutoCAD.McpPython\src\autocad_mcp\__init__.py"
if (Test-Path $initPath) {
    (Get-Content $initPath -Raw) -replace '__version__\s*=\s*"[^"]+"', "__version__ = `"$cleanVersion`"" | Set-Content $initPath -NoNewline
    Write-Host "  [OK] $initPath -> $cleanVersion" -ForegroundColor Green
}

foreach ($csproj in @("AutoCAD.Gateway\AutoCAD.Gateway.csproj", "AutoCAD.McpServer\AutoCAD.McpServer.csproj")) {
    if (Test-Path $csproj) {
        (Get-Content $csproj -Raw) -replace '<Version>[^<]+</Version>', "<Version>$cleanVersion</Version>" | Set-Content $csproj -NoNewline
        Write-Host "  [OK] $csproj -> $cleanVersion" -ForegroundColor Green
    }
}

foreach ($fallback in @("AutoCAD.Gateway\Main.cs", "AutoCAD.Gateway\Server\RequestRouter.cs", "AutoCAD.McpServer\Program.cs")) {
    if (Test-Path $fallback) {
        (Get-Content $fallback -Raw) -replace 'ToString\(3\)\s*\?\?\s*"[^"]+"', "ToString(3) ?? `"$cleanVersion`"" | Set-Content $fallback -NoNewline
        Write-Host "  [OK] $fallback fallback -> $cleanVersion" -ForegroundColor Green
    }
}

$bundleScript = "scripts\install-bundle.ps1"
if (Test-Path $bundleScript) {
    (Get-Content $bundleScript -Raw) -replace 'else\s*\{\s*"[0-9]+\.[0-9]+\.[0-9]+\.[0-9]+"\s*\}', "else { `"$cleanVersion.0`" }" | Set-Content $bundleScript -NoNewline
    Write-Host "  [OK] $bundleScript fallback -> $cleanVersion.0" -ForegroundColor Green
}

$mcpReadme = "AutoCAD.McpServer\README.md"
if (Test-Path $mcpReadme) {
    (Get-Content $mcpReadme -Raw) -replace '\*\*Version:\*\*\s*[0-9]+\.[0-9]+\.[0-9]+', "**Version:** $cleanVersion" | Set-Content $mcpReadme -NoNewline
    Write-Host "  [OK] $mcpReadme -> $cleanVersion" -ForegroundColor Green
}

Write-Host "`n>>> Version $cleanVersion synchronized cleanly." -ForegroundColor Cyan
Write-Host "Next steps before tagging:"
Write-Host "  1. Update CHANGELOG.md with ## [$cleanVersion] - YYYY-MM-DD"
Write-Host "  2. Run: uv lock --directory AutoCAD.McpPython --offline"
Write-Host "  3. Stage reviewed files via git add -p / explicit paths, open a PR, and tag after merge."
