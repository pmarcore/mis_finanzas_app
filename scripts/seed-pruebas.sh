#!/usr/bin/env sh
# Deja el ambiente de pruebas con la foto de octubre de la planilla.
# Borra movimientos, fijos, presupuestos, categorías y ciclos de tarjeta antes de cargar.
#   ./scripts/seed-pruebas.sh [URL de la API] [usuario] [contraseña]
#   (por defecto http://localhost:5080, pablo, pruebas: el usuario de prueba de docker compose)
# OJO: no correrlo contra la API de la nube, borra los datos reales.
set -eu
API_URL="${1:-http://localhost:5080}"
USUARIO="${2:-${USUARIO_1:-pablo}}"
CLAVE="${3:-${CLAVE_1:-pruebas}}"
DIR="$(cd "$(dirname "$0")/.." && pwd)"

curl -fsS "$API_URL/health" >/dev/null || { echo "La API no responde en $API_URL" >&2; exit 1; }
TOKEN="$(curl -fsS -X POST "$API_URL/auth/login" -H 'content-type: application/json' \
  --data "{\"username\":\"$USUARIO\",\"password\":\"$CLAVE\"}" | sed -n 's/.*"token":"\([^"]*\)".*/\1/p')"
[ -n "$TOKEN" ] || { echo "No se pudo entrar con el usuario $USUARIO" >&2; exit 1; }
curl -fsS -X POST "$API_URL/import/sheet?replace=true" \
  -H 'content-type: application/json' -H "authorization: Bearer $TOKEN" \
  --data @"$DIR/data/import/planilla-2026-10.json"
echo
echo "Listo: $API_URL tiene la foto de octubre."
