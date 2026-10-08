# Finanzas Familia y Memey

App para manejar la caja familiar y la de Memey: caja actual, proyección a fin de mes y trimestral, pagos de tarjeta, gastos fijos y presupuesto vs real con fecha de corte.

- `backend/`: API en .NET 10 con SQLite (EF Core). Calcula todo.
- `mobile/`: app React Native con Expo. Muestra y carga datos.
- `docs/arquitectura.md`: diagrama y reglas de negocio.

## Backend

Requisitos: .NET SDK 10.

```bash
cd backend
dotnet tool restore
dotnet test
dotnet run --project src/Finanzas.Api   # http://localhost:5080
```

La base `finanzas.db` se crea sola al arrancar (migraciones de EF Core). Para una migración nueva:

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

Proyecto inicializado: modelo de datos, motor de caja con tests y endpoints base. Siguiente paso: completar endpoints de gastos fijos, presupuesto y reporte, y conectar las pantallas.
