# Deja el ambiente de pruebas con la foto de octubre de la planilla (versión Windows de seed-pruebas.sh).
# Borra movimientos, fijos, presupuestos, categorías y ciclos de tarjeta antes de cargar.
#   .\scripts\seed-pruebas.ps1 [URL de la API]   (por defecto http://localhost:5080)
# Si PowerShell no deja correr scripts:
#   powershell -ExecutionPolicy Bypass -File .\scripts\seed-pruebas.ps1
param([string]$ApiUrl = "http://localhost:5080")
$ErrorActionPreference = "Stop"
$ApiUrl = $ApiUrl.TrimEnd("/")
$json = Join-Path $PSScriptRoot "..\data\import\planilla-2026-10.json"

try {
    Invoke-RestMethod "$ApiUrl/health" | Out-Null
} catch {
    Write-Error "La API no responde en $ApiUrl. ¿Está corriendo docker compose?"
}

$result = Invoke-RestMethod -Method Post -Uri "$ApiUrl/import/sheet?replace=true" `
    -ContentType "application/json; charset=utf-8" -InFile $json
$result | ConvertTo-Json -Compress
Write-Host "Listo: $ApiUrl tiene la foto de octubre."
