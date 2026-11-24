<#
.SYNOPSIS
    Generates a Markdown release notes file (without emojis) for GitHub Releases.
.EXAMPLE
    .\scripts\generate-release-notes.ps1 -Version "2.5.0" -Tag "v2.5.0" -OutputPath "release_body.md"
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
- AutoCAD.Gateway-Release.zip: Native .NET 10 AutoCAD extension plugin (acad.exe NETLOAD / Autoloader bundle).
- AutoCAD.McpServer-Release.zip: Standalone C# .NET 10 MCP server (STDIO).
- Python Distributions: Python FastMCP wheel and sdist packages (PyPI: luiccian-autocad-mcp).
- SHA256SUMS.txt: SHA-256 verification digests for all release assets.

## Highlights
- 89 typed MCP tools (75 canonical + 14 aliases) with selective exposure via AUTOCAD_MCP_TOOL_MODE (granular, compound, core).
- Native ObjectARX entity, layer, drafting, block, viewport, PDF plotting, screenshot, and Civil 3D transactions.
- Hardened loopback TcpListener server with constant-time X-Gateway-Token auth, Host/Origin guards, and optional AUTOCAD_GATEWAY_RESTRICTED_MODE=1.
"@

Set-Content -Path $OutputPath -Value $content -Encoding utf8
Write-Host ">>> Generated clean release notes at $OutputPath"
