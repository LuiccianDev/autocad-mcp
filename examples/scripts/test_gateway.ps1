<#
.SYNOPSIS
    Script de diagnóstico para verificar la comunicación con AutoCAD AI Gateway.
.DESCRIPTION
    Envía una solicitud GET de verificación de estado y una orden POST de dibujo
    utilizando el token de seguridad configurado.
#>

param(
    [string]$GatewayUrl = "http://localhost:5055",
    [string]$Token = $env:AUTOCAD_GATEWAY_TOKEN
)

if ([string]::IsNullOrWhiteSpace($Token)) {
    $Token = "autocad-gateway-token-2027"
}

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "   Diagnóstico de Conexión: AutoCAD AI Gateway   " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan
Write-Host "URL Destino: $GatewayUrl"
Write-Host "Token: $Token"
Write-Host ""

$headers = @{
    "X-Gateway-Token" = $Token
}

# 1. Probar Endpoint de Información del Dibujo
Write-Host "[1/2] Consultando información del dibujo (/context/drawing)..." -NoNewline
try {
    $drawingInfo = Invoke-RestMethod -Uri "$GatewayUrl/context/drawing" -Method Get -Headers $headers -TimeoutSec 5
    Write-Host " [OK]" -ForegroundColor Green
    Write-Host "  - Archivo DWG : $($drawingInfo.DrawingName)" -ForegroundColor Gray
    Write-Host "  - Unidades    : $($drawingInfo.UnitsCode) (4 = Metros)" -ForegroundColor Gray
} catch {
    Write-Host " [FALLÓ]" -ForegroundColor Red
    Write-Host "  Error al conectar con el Gateway: $_" -ForegroundColor Red
    Write-Host "  Verifica que AutoCAD esté abierto y que hayas ejecutado NETLOAD con la DLL del Gateway." -ForegroundColor Yellow
    exit 1
}

# 2. Probar Endpoint de Ejecución de Comandos
Write-Host "[2/2] Probando inyección de comando de prueba (/execute)..." -NoNewline
$testPayload = @{
    command = "_.ZOOM`nE`n"
} | ConvertTo-Json

try {
    $execResponse = Invoke-RestMethod -Uri "$GatewayUrl/execute" -Method Post -Headers $headers -ContentType "application/json" -Body $testPayload -TimeoutSec 5
    Write-Host " [OK]" -ForegroundColor Green
    Write-Host "  Respuesta del Gateway: $($execResponse.status)" -ForegroundColor Gray
} catch {
    Write-Host " [FALLÓ]" -ForegroundColor Red
    Write-Host "  Error al ejecutar comando: $_" -ForegroundColor Red
    exit 1
}

Write-Host ""
Write-Host "¡El Gateway está activo, autenticado y respondiendo correctamente!" -ForegroundColor Green
