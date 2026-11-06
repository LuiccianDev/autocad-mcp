#!/usr/bin/env bash
# Script de diagnóstico rápido en Bash / cURL para AutoCAD AI Gateway

GATEWAY_URL="${GATEWAY_URL:-http://localhost:5055}"
GATEWAY_TOKEN="${AUTOCAD_GATEWAY_TOKEN:-autocad-gateway-token-2027}"

echo "================================================="
echo "   Diagnóstico de Conexión: AutoCAD AI Gateway   "
echo "================================================="
echo "URL Destino: $GATEWAY_URL"
echo "Token: $GATEWAY_TOKEN"
echo ""

echo -n "[1/2] Consultando información del dibujo (/context/drawing)... "
HTTP_RESPONSE=$(curl -s -w "\n%{http_code}" -H "X-Gateway-Token: $GATEWAY_TOKEN" "$GATEWAY_URL/context/drawing")
HTTP_BODY=$(echo "$HTTP_RESPONSE" | sed '$d')
HTTP_STATUS=$(echo "$HTTP_RESPONSE" | tail -n 1)

if [ "$HTTP_STATUS" -eq 200 ]; then
    echo "[OK]"
    echo "Respuesta: $HTTP_BODY"
else
    echo "[FALLÓ] (HTTP $HTTP_STATUS)"
    echo "Verifica que AutoCAD esté abierto con el Gateway cargado vía NETLOAD."
    exit 1
fi

echo -n "[2/2] Probando inyección de comando de prueba (/execute)... "
EXEC_RESPONSE=$(curl -s -w "\n%{http_code}" \
    -X POST \
    -H "Content-Type: application/json" \
    -H "X-Gateway-Token: $GATEWAY_TOKEN" \
    -d '{"command": "_.ZOOM\nE\n"}' \
    "$GATEWAY_URL/execute")

EXEC_BODY=$(echo "$EXEC_RESPONSE" | sed '$d')
EXEC_STATUS=$(echo "$EXEC_RESPONSE" | tail -n 1)

if [ "$EXEC_STATUS" -eq 200 ]; then
    echo "[OK]"
    echo "Respuesta: $EXEC_BODY"
    echo ""
    echo "¡El Gateway está activo, autenticado y respondiendo correctamente!"
else
    echo "[FALLÓ] (HTTP $EXEC_STATUS)"
    exit 1
fi
