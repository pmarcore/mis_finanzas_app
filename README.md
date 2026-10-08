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
cp .env.example .env    # EXPO_PUBLIC_API_URL apunta a la API
npm install
npx expo start
```

## Estado

API con dashboard, movimientos, tarjetas, gastos fijos, presupuesto, reporte presupuestado vs real e importación de la planilla. Octubre importado y comparado contra la planilla en los tests.
