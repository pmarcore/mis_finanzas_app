# Finanzas — app móvil

App en React Native + Expo (TypeScript) para el proyecto de finanzas personales. Es un **esqueleto**: las pantallas tienen estructura real y están conectadas a un cliente de API tipado, pero toda la lógica de negocio (saldos, proyecciones, vencimientos de tarjeta, uso del presupuesto) la calcula el backend .NET. El cliente sólo muestra y envía datos.

## Cómo correrla

```bash
npm install
cp .env.example .env      # y ajustá la URL si hace falta
npx expo start
```

Después abrí la app con Expo Go (escaneando el QR) o con un emulador (`a` para Android, `i` para iOS).

Chequeo de tipos:

```bash
npm run typecheck         # equivale a npx tsc --noEmit
```

## Variable de entorno

| Variable | Default | Uso |
|---|---|---|
| `EXPO_PUBLIC_API_URL` | `http://localhost:5080` | URL base del backend .NET |

Ojo: desde un teléfono físico `localhost` es el propio teléfono. Usá la IP de tu máquina en la red local (p. ej. `http://192.168.0.10:5080`). En el emulador de Android, `http://10.0.2.2:5080`.

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
    │   └── scope.tsx        # ámbito seleccionado (familia/memey/total) y fecha de corte
    ├── navigation/
    │   ├── RootNavigator.tsx  # tabs: Inicio, Movimientos, Nuevo, Fijos, Presupuesto
    │   │                      # stack: Reporte, Tarjetas, Configuración (engranaje)
    │   └── types.ts
    ├── screens/             # una pantalla por archivo
    ├── components/          # ScopeSelector, MoneyText, Card y otros básicos
    ├── utils/format.ts      # formatARS ("$ 1.234.567") y helpers de fechas
    └── theme.ts             # colores claro/oscuro
```

## Convenciones

- Montos en pesos (`number`), fechas `AAAA-MM-DD`, meses `AAAA-MM`.
- Texto de la interfaz en español (Argentina).
- Agregar dependencias con `npx expo install <paquete>` para que las versiones coincidan con el SDK.
- No duplicar cálculos del backend en el cliente.
