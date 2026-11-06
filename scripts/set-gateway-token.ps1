[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$Token,

    [switch]$UpdateExampleConfigs
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = "luiccian-" + [System.Guid]::NewGuid().ToString("N")
    Write-Host ""
    Write-Host "[+] Generando nuevo token seguro aleatorio..." -ForegroundColor Cyan
} else {
    $Token = $Token.Trim()
    Write-Host ""
    Write-Host "[+] Aplicando token suministrado por el usuario..." -ForegroundColor Cyan
}

# 1. Guardar en las variables de entorno de usuario en Windows (persistente entre reinicios)
[System.Environment]::SetEnvironmentVariable("AUTOCAD_GATEWAY_TOKEN", $Token, [System.EnvironmentVariableTarget]::User)
$env:AUTOCAD_GATEWAY_TOKEN = $Token

Write-Host "=======================================================" -ForegroundColor Green
Write-Host "  AUTOCAD AI GATEWAY - TOKEN CONFIGURADO CON EXITO" -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Green
Write-Host "Variable registrada en Windows: AUTOCAD_GATEWAY_TOKEN" -ForegroundColor White
Write-Host "Token activo:                   $Token" -ForegroundColor Yellow
Write-Host ""

# 2. Si se solicito actualizar las configuraciones de ejemplo
if ($UpdateExampleConfigs) {
    $repoRoot = Split-Path -Parent $PSScriptRoot
    $claudeConfig = Join-Path $repoRoot "examples\configs\claude_desktop_config.json"
    $mcpConfig = Join-Path $repoRoot "examples\configs\mcp_config.json"

    foreach ($file in @($claudeConfig, $mcpConfig)) {
        if (Test-Path $file) {
            $content = Get-Content $file -Raw
            $updated = $content -replace '"AUTOCAD_GATEWAY_TOKEN":\s*"[^"]*"', "`"AUTOCAD_GATEWAY_TOKEN`": `"$Token`""
            Set-Content -Path $file -Value $updated -Encoding UTF8
            Write-Host "[OK] Actualizado archivo: $file" -ForegroundColor Gray
        }
    }
}

Write-Host ""
Write-Host "Copia este bloque en la seccion env de tu configuracion de IA:" -ForegroundColor Cyan
Write-Host " `"env`": {" -ForegroundColor DarkYellow
Write-Host "   `"AUTOCAD_GATEWAY_PORT`": `"5055`"," -ForegroundColor DarkYellow
Write-Host "   `"AUTOCAD_GATEWAY_TOKEN`": `"$Token`"" -ForegroundColor DarkYellow
Write-Host " }" -ForegroundColor DarkYellow
Write-Host ""
