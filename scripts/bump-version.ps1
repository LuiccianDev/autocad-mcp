<#
.SYNOPSIS
    Sincroniza y actualiza la versión del ecosistema autocad-mcp (pyproject.toml, .csproj y Git).
.EXAMPLE
    .\scripts\bump-version.ps1 -NewVersion "2.3.1"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$NewVersion
)

$ErrorActionPreference = "Stop"

# Limpiar prefijo 'v' si fue proporcionado
$cleanVersion = $NewVersion.TrimStart('v').Trim()

Write-Host ">>> Sincronizando ecosistema a la version: $cleanVersion" -ForegroundColor Cyan

# 1. Actualizar AutoCAD.McpPython/pyproject.toml
$pyprojectPath = "AutoCAD.McpPython\pyproject.toml"
if (Test-Path $pyprojectPath) {
    (Get-Content $pyprojectPath) -replace 'version\s*=\s*"[^"]+"', "version = `"$cleanVersion`"" | Set-Content $pyprojectPath
    Write-Host "  [OK] $pyprojectPath -> $cleanVersion" -ForegroundColor Green
}

# 2. Actualizar AutoCAD.Gateway/AutoCAD.Gateway.csproj
$gatewayCsproj = "AutoCAD.Gateway\AutoCAD.Gateway.csproj"
if (Test-Path $gatewayCsproj) {
    (Get-Content $gatewayCsproj) -replace '<Version>[^<]+</Version>', "<Version>$cleanVersion</Version>" `
                                 -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$cleanVersion.0</AssemblyVersion>" `
                                 -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$cleanVersion.0</FileVersion>" `
                                 -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$cleanVersion</InformationalVersion>" | Set-Content $gatewayCsproj
    Write-Host "  [OK] $gatewayCsproj -> $cleanVersion" -ForegroundColor Green
}

# 3. Actualizar AutoCAD.McpServer/AutoCAD.McpServer.csproj
$mcpCsproj = "AutoCAD.McpServer\AutoCAD.McpServer.csproj"
if (Test-Path $mcpCsproj) {
    (Get-Content $mcpCsproj) -replace '<Version>[^<]+</Version>', "<Version>$cleanVersion</Version>" `
                             -replace '<AssemblyVersion>[^<]+</AssemblyVersion>', "<AssemblyVersion>$cleanVersion.0</AssemblyVersion>" `
                             -replace '<FileVersion>[^<]+</FileVersion>', "<FileVersion>$cleanVersion.0</FileVersion>" `
                             -replace '<InformationalVersion>[^<]+</InformationalVersion>', "<InformationalVersion>$cleanVersion</InformationalVersion>" | Set-Content $mcpCsproj
    Write-Host "  [OK] $mcpCsproj -> $cleanVersion" -ForegroundColor Green
}

Write-Host "`n>>> Version $cleanVersion sincronizada exitosamente en todos los proyectos." -ForegroundColor Cyan
Write-Host "Para publicar la nueva version ejecute:"
Write-Host "  git add ."
Write-Host "  git commit -m `"chore: bump version to $cleanVersion`""
Write-Host "  git tag -a v$cleanVersion -m `"Release v$cleanVersion`""
Write-Host "  git push origin master --tags"
