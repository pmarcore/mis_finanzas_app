#!/usr/bin/env sh
# Deja el ambiente de pruebas con la foto de octubre de la planilla.
# Borra movimientos, fijos, presupuestos, categorías y ciclos de tarjeta antes de cargar.
#   ./scripts/seed-pruebas.sh [URL de la API]   (por defecto http://localhost:5080)
set -eu
API_URL="${1:-http://localhost:5080}"
DIR="$(cd "$(dirname "$0")/.." && pwd)"

curl -fsS "$API_URL/health" >/dev/null || { echo "La API no responde en $API_URL" >&2; exit 1; }
curl -fsS -X POST "$API_URL/import/sheet?replace=true" \
  -H 'content-type: application/json' \
  --data @"$DIR/data/import/planilla-2026-10.json"
echo
echo "Listo: $API_URL tiene la foto de octubre."
