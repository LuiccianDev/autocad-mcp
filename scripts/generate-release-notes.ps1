<#
.SYNOPSIS
    Genera el archivo markdown de notas de lanzamiento (sin emojis) para el GitHub Release.
.EXAMPLE
    .\scripts\generate-release-notes.ps1 -Version "2.3.0" -Tag "v2.3.0" -OutputPath "release_body.md"
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [Parameter(Mandatory = $true)]
    [string]$Tag,

    [Parameter(Mandatory = $false)]
    [string]$OutputPath = "release_body.md"
)

$ErrorActionPreference = "Stop"

$content = @"
# AutoCAD 2027 & Civil 3D AI Gateway and MCP Server - Version $Version

Automated release $Tag for the AutoCAD AI Gateway and MCP Server ecosystem.

## Artifacts Included
- AutoCAD.Gateway-Release.zip: Native .NET 10 AutoCAD extension plugin (acad.exe NETLOAD).
- AutoCAD.McpServer-Release.zip: Standalone C# .NET 10 MCP server (STDIO).
- Python Distributions: Python FastMCP wheel and sdist packages.

## Highlights
- 45 typed MCP tools covering inspection, geometry, layers, calculations, layouts, and Civil 3D.
- High-performance batch drafting engine (dibujar_geometria_lote).
- Thread-safe command dispatching with Application.Idle synchronization.
- Zero-secret security with X-Gateway-Token authentication.
"@

Set-Content -Path $OutputPath -Value $content -Encoding utf8
Write-Host ">>> Generated clean release notes at $OutputPath"
