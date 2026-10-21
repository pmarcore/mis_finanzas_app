# Desarrollo, pruebas y CI

Cómo levantar todo está en el [README](../README.md) (backend, mobile, ambiente de pruebas con Docker, admin de la
base, probar en la PC sin celular). Acá van las convenciones y cómo se prueba.

## Estructura del repositorio

```
.
├── backend/
│   ├── Finanzas.slnx
│   ├── Dockerfile
│   ├── dotnet-tools.json            # dotnet-ef
│   ├── src/
│   │   ├── Finanzas.Domain/         # entidades y reglas puras
│   │   ├── Finanzas.Infrastructure/ # EF Core, migraciones, importación, gastos fijos
│   │   └── Finanzas.Api/            # Minimal API, DTOs, Program.cs
│   └── tests/Finanzas.Tests/        # xUnit
├── mobile/                          # Expo + React Native (ver mobile/README.md)
├── data/import/                     # foto JSON de la planilla de octubre
├── scripts/                         # seed del ambiente de pruebas
├── docs/                            # esta documentación
├── docker-compose.yml
└── .github/workflows/ci.yml
```

## Convenciones

- **Idioma:** textos de la interfaz, mensajes de error de la API, comentarios y documentación en español (Argentina).
  Los nombres de clases y propiedades del backend están en inglés; los de pantallas de la app, en español.
- **Montos:** centavos (`long`) en dominio y base; pesos (`decimal`/`number`) en la API y la app. La conversión vive en
  `Money` (`Dtos.cs`).
- **Fechas:** `DateOnly` / `yyyy-MM-dd`; meses `yyyy-MM`. La fecha de corte no se guarda: es hoy por defecto y la app
  la manda en cada consulta.
- **Ámbito:** siempre `familia` o `memey`; no hay vista total.
- **Reglas de negocio:** en `Finanzas.Domain`, como funciones puras. La app no calcula saldos.
- **Conceptos de plata nuevos:** se agregan como tipos de operación (datos), no como enums en código.
- **Catálogos en uso:** no se borran, se desactivan (`Active`).
- **Formato:** `.editorconfig` en la raíz.

## Tests

`cd backend && dotnet test` (27 tests).

| Archivo | Qué prueba |
| --- | --- |
| `CashFlowTests.cs` | Reglas del dominio sin base: vencimiento propuesto, cuotas y redondeo, compra con tarjeta que no toca caja hasta el vencimiento, resumen real vs modelado, pago parcial, transferencias, gastos fijos, tipos configurables, ahorro |
| `PlanillaOctubreTests.cs` | Levanta la API en memoria (`WebApplicationFactory`) con una base SQLite temporal, importa `data/import/planilla-2026-10.json` y compara caja, pagos de tarjeta, presupuesto vs real, proyección y flujo de caja contra los valores de la planilla al 08/10/2026 |
| `EdicionMovimientosTests.cs` | Editar y borrar movimientos: un gasto que cambia caja y presupuesto vs real, un ingreso que cambia monto y fecha, una compra que pasa a tarjeta y sale de la caja hacia el resumen, editar o borrar un pago de tarjeta, y ediciones inválidas que dejan el movimiento como estaba |
| `ConfiguracionTests.cs` | Tipos de operación por ámbito, cambio de efecto que recalcula movimientos, ahorro e inversión aparte de la caja, alta y desactivación de categorías y medios de pago |
| `VigenciaEIncrementosTests.cs` | Gastos fijos con vigencia desde-hasta e incrementos % acumulados: montos por mes, previstos generados por la API y validaciones (primer cambio es un monto y rige desde el inicio, ningún cambio después del hasta, hasta no anterior a desde) |
| `PresupuestoMesesFuturosTests.cs` | Proyectar presupuesto: fuente presupuesto (con herencia) o real de un mes cerrado, ajuste % único y redondeo a $1.000, fijos con incrementos y vigencia (Fijo/Deuda vs. Variable), guardado de varios meses sin tocar la otra entidad, aviso de herencia y validaciones |
| `PresupuestoPorEntidadTests.cs` | Presupuesto de Familia y de Memey guardados y reportados por separado, detalle de movimientos de una categoría del presupuesto vs. real (suma igual al real) y filtros del listado por categoría y medio de pago |

La app se valida con `cd mobile && npx tsc --noEmit` (`npm run typecheck`).

## CI

`.github/workflows/ci.yml` corre en cada push a `main` y en cada PR, con tres jobs:

1. **backend:** `dotnet test` con .NET 10.
2. **mobile:** `npm ci` y `npx tsc --noEmit` con Node 22.
3. **docker:** `docker build` de la imagen de la API.

## Cambios en la base

1. Cambiar la entidad en `Finanzas.Domain/Entities.cs` y, si hace falta, el mapeo en `FinanzasDbContext.OnModelCreating`.
2. Crear la migración desde `backend/`:
   `dotnet tool restore && dotnet ef migrations add <Nombre> -p src/Finanzas.Infrastructure -s src/Finanzas.Api`
3. La API la aplica sola al arrancar. Actualizar el [DER](base-de-datos.md).

## Pendientes conocidos

- **Autenticación:** la API no tiene login y el CORS está abierto. El cliente ya manda un token bearer si se le
  configura (`setAuthToken`). Antes de publicar la API fuera de la red local hay que agregar autenticación.
- **Invertir Online:** interfaz `IInvestmentProvider` lista, sin implementación real (ver
  [arquitectura.md](arquitectura.md#inversiones-e-invertir-online)).
- **Resúmenes reales de tarjeta:** `Statements.ConfirmedAmount` solo se carga por importación; no hay endpoint para
  editarlo desde la app.
