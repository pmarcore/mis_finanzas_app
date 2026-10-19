# Finanzas — app móvil

App en React Native + Expo (TypeScript) para el proyecto de finanzas personales. Las pantallas están conectadas a un cliente de API tipado, pero toda la lógica de negocio (saldos, proyecciones, vencimientos de tarjeta, uso del presupuesto) la calcula el backend .NET. El cliente sólo muestra y envía datos.

## Cómo correrla

```bash
npm install
npx expo start            # o npx expo start --web para abrirla en el navegador (http://localhost:8081)
```

Después abrí la app con Expo Go (escaneando el QR), con un emulador (`a` para Android, `i` para iOS) o en el navegador. Más detalle en el [README](../README.md) de la raíz.

Chequeo de tipos:

```bash
npm run typecheck         # equivale a npx tsc --noEmit
```

## Variable de entorno

| Variable | Default | Uso |
|---|---|---|
| `EXPO_PUBLIC_API_URL` | IP de la PC que corre `npx expo start`, puerto 5080 | URL base del backend .NET. Opcional: definila en `.env` (ver `.env.example`) solo si la API corre en otra máquina |

Sin la variable, la app toma la IP de la PC que sirve Expo (`src/api/client.ts`), así que desde un teléfono en el mismo Wi-Fi o desde el emulador llega a la API sin configurar nada.

Las variables `EXPO_PUBLIC_*` se incrustan al compilar: después de cambiar `.env`, reiniciá `npx expo start` (con `-c` si no toma el cambio).

## Estructura

```
mobile/
├── App.tsx                  # QueryClientProvider + contexto de ámbito + NavigationContainer
├── .env.example
└── src/
    ├── api/
    │   ├── client.ts        # fetch con URL base, JSON y token bearer (placeholder)
    │   ├── types.ts         # tipos del dominio (espejo del backend)
    │   └── endpoints.ts     # hooks de react-query por endpoint
    ├── state/
    │   └── scope.tsx        # ámbito seleccionado (familia/memey) y fecha de corte
    ├── navigation/
    │   ├── RootNavigator.tsx  # tabs: Inicio, Movimientos, Nuevo, Fijos, Presupuesto
    │   │                      # stack: Reporte, Tarjetas, Configuración (engranaje), Tipos de operación,
    │   │                      # Categorías, Medios de pago, Ahorro e inversión, Editar movimiento
    │   └── types.ts
    ├── screens/             # una pantalla por archivo
    ├── components/          # Select (combos), ScopeSelector, CutoffControl, MoneyText, Card y otros
    ├── utils/
    │   ├── format.ts        # formatARS ("$ 1.234.567") y helpers de fechas
    │   └── alert.ts         # Alert que también funciona en el navegador
    └── theme.ts             # colores claro/oscuro
```

## Contrato con el backend

La fuente de verdad es `backend/src/Finanzas.Api/Endpoints.cs` y `Dtos.cs`; `src/api/types.ts` es su espejo.

- JSON en camelCase; ids numéricos; enums como strings camelCase:
  - `scope`: `familia` | `memey` (no hay vista total).
  - `operationTypeId`: id de un tipo de operación configurable (`GET /operation-types`); su `kind` (`movimiento` | `pagoTarjeta` | `transferencia` | `ahorro` | `inversion`) y `effect` (`suma` | `resta`) definen cómo mueve la caja.
  - `status`: `realizado` | `previsto`; tipo de medio de pago: `debito` | `efectivo` | `billetera` | `tarjeta`.
  - Categorías: `kind` `ingreso` | `gasto`; `budgetType` `fijo` | `variable` | `deuda`.
- Compra con tarjeta: `firstDueDate` es opcional (el backend lo propone con los cierres; si falta el cierre responde 400 con un mensaje).
- `PUT /transactions/{id}` recibe el mismo cuerpo que `POST /transactions` y valida igual; `DELETE /transactions/{id}` borra el movimiento y sus impactos en caja. Pantalla: Editar movimiento (tocar una fila en Movimientos).
- `pagoTarjeta`: requiere `paidCardId` y `firstDueDate` (vencimiento que se paga).
- `POST/PUT /recurring-rules` devuelven `{ rule, missingCycles }`; la app avisa si faltan cierres.
- `GET /reports/budget-vs-actual` devuelve `{ rows, totalBudget, totalActual, totalProjected, ... }` con una `alert` por fila.
- `PUT /settings` guarda piso de caja, meta de ahorro y umbrales de alerta; cada caja se guarda con `PUT /settings/cash-boxes/{id}`.
- Los errores 400/404 del backend son strings en español: el cliente los muestra tal cual (`ApiError.message`).

## Convenciones

- Montos en pesos (`number`, hasta 2 decimales), fechas `AAAA-MM-DD`, meses `AAAA-MM`.
- Texto de la interfaz en español (Argentina).
- Agregar dependencias con `npx expo install <paquete>` para que las versiones coincidan con el SDK.
- No duplicar cálculos del backend en el cliente.
