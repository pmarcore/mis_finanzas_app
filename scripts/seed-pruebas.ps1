# Deja el ambiente de pruebas con la foto de octubre de la planilla (versión Windows de seed-pruebas.sh).
# Borra movimientos, fijos, presupuestos, categorías y ciclos de tarjeta antes de cargar.
#   .\scripts\seed-pruebas.ps1 [URL de la API] [usuario] [contraseña]
#   (por defecto http://localhost:5080, pablo, pruebas: el usuario de prueba de docker compose)
# OJO: no correrlo contra la API de la nube, borra los datos reales.
# Si PowerShell no deja correr scripts:
#   powershell -ExecutionPolicy Bypass -File .\scripts\seed-pruebas.ps1
param([string]$ApiUrl = "http://localhost:5080", [string]$Usuario = "pablo", [string]$Clave = "pruebas")
$ErrorActionPreference = "Stop"
$ApiUrl = $ApiUrl.TrimEnd("/")
$json = Join-Path $PSScriptRoot "..\data\import\planilla-2026-10.json"

try {
    Invoke-RestMethod "$ApiUrl/health" | Out-Null
} catch {
    Write-Error "La API no responde en $ApiUrl. ¿Está corriendo docker compose?"
}

$login = Invoke-RestMethod -Method Post -Uri "$ApiUrl/auth/login" -ContentType "application/json" `
    -Body (@{ username = $Usuario; password = $Clave } | ConvertTo-Json)
$result = Invoke-RestMethod -Method Post -Uri "$ApiUrl/import/sheet?replace=true" `
    -ContentType "application/json; charset=utf-8" -InFile $json -Headers @{ Authorization = "Bearer $($login.token)" }
$result | ConvertTo-Json -Compress
Write-Host "Listo: $ApiUrl tiene la foto de octubre."
