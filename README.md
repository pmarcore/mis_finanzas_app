# Finanzas Familia y Memey

App para manejar la caja familiar y la de Memey: caja actual, proyección a fin de mes y trimestral, pagos de tarjeta, gastos fijos y presupuesto vs real con fecha de corte.

- `backend/`: API en .NET 10 con SQLite (EF Core). Calcula todo.
- `mobile/`: app React Native con Expo. Muestra y carga datos.
- `docs/arquitectura.md`: diagrama, reglas de negocio y endpoints.
- `data/import/`: foto de octubre de la planilla, lista para importar.

## Backend

Requisitos: .NET SDK 10.

```bash
cd backend
dotnet tool restore
dotnet test
dotnet run --project src/Finanzas.Api   # http://localhost:5080
```

La base `finanzas.db` se crea sola al arrancar (migraciones de EF Core). Para cargar octubre desde la planilla, ver `data/import/README.md`. Para una migración nueva:

```bash
dotnet ef migrations add <Nombre> -p src/Finanzas.Infrastructure -s src/Finanzas.Api -o Migrations
```

## Mobile

Requisitos: Node 20 o superior y la app Expo Go en el teléfono.

```bash
cd mobile
npm install
npx expo start
```

## Ambiente de pruebas (local)

Requisitos: Docker Desktop abierto (en Windows, con el motor de Linux; el ícono de la ballena tiene que decir "Engine running"). Levanta la API con su propia base, separada de la de desarrollo, y la carga con la foto de octubre.

```bash
docker compose up -d --build
```

Para cargar la foto de octubre (y volver a ese punto cuando quieras empezar de cero):

- Windows (PowerShell): `powershell -ExecutionPolicy Bypass -File .\scripts\seed-pruebas.ps1`
- Mac o Linux: `./scripts/seed-pruebas.sh`

Para probar en el celular con Expo Go, el teléfono tiene que estar en el mismo Wi-Fi que la PC. La app busca la API en la misma PC que corre `npx expo start` (puerto 5080), así que no hace falta configurar la IP; si cambia, alcanza con reiniciar Expo y volver a escanear el QR. `EXPO_PUBLIC_API_URL` en `mobile/.env` solo hace falta si la API corre en otra máquina, y después de cambiarlo hay que reiniciar Expo con `npx expo start -c`. Si Windows pregunta por el firewall al levantar Docker o Expo, permití redes privadas.

Si la app dice "No se pudo conectar con la API en ...", abrí `http://<IP de la PC>:5080/health` en el navegador del celular. Si ahí tampoco carga, el problema es la red o el firewall de Windows (la red Wi-Fi tiene que estar como "Privada"); si carga, revisá que la URL del mensaje sea la de `.env`.

## Estado

API con dashboard, movimientos, tarjetas, gastos fijos, presupuesto, reporte presupuestado vs real e importación de la planilla. Octubre importado y comparado contra la planilla en los tests.
