# Arquitectura

La app mobile solo muestra y carga datos; la API .NET calcula caja, cuotas y proyecciones con las reglas de la planilla.
Ver también el [DER](base-de-datos.md), los [diagramas de clases](clases-backend.md), la [app mobile](app-mobile.md) y
las [tecnologías y versiones](tecnologias.md).

## Vista general

```mermaid
flowchart LR
  subgraph Mobile["App mobile (React Native + Expo)"]
    UI["Pantallas: Inicio, Movimientos, Nuevo, Fijos, Presupuesto<br/>Reporte, Tarjetas, Configuración, Ahorro"]
    RQ["React Query (caché)"]
    CL["api/client.ts (fetch)"]
  end
  subgraph API[".NET 10 API"]
    EP["Endpoints Minimal API<br/>Endpoints.cs · Catalogs.cs · Dtos.cs"]
    DOM["Domain<br/>CardCalendar · CashImpactBuilder<br/>RecurringRuleExpander · CashFlowCalculator<br/>Budgeting · Savings"]
    INF["Infrastructure<br/>EF Core 10 + migraciones<br/>RecurringRuleSync · SheetImport"]
  end
  DB[("SQLite<br/>finanzas.db")]
  GS["Planilla Google<br/>(foto JSON en data/import)"]
  IOL["Invertir Online<br/>(pendiente de conectar)"]
  UI --> RQ --> CL -->|HTTP JSON| EP
  EP --> DOM
  EP --> INF -->|SQL| DB
  GS -.->|POST /import/sheet| EP
  EP -.->|IInvestmentProvider| IOL
```

## Capas

| Capa | Proyecto o carpeta | Qué hace | Qué no hace |
| --- | --- | --- | --- |
| Presentación | `mobile/` | Pantallas, formularios, caché de consultas, formato de pesos y fechas | No calcula saldos ni proyecciones |
| API | `backend/src/Finanzas.Api` | Rutas HTTP, validación de entrada, DTOs (pesos ↔ centavos), arranque | No guarda reglas de negocio propias |
| Dominio | `backend/src/Finanzas.Domain` | Entidades y reglas puras: impactos en caja, cuotas, proyecciones, presupuesto | No conoce la base ni HTTP |
| Infraestructura | `backend/src/Finanzas.Infrastructure` | EF Core + SQLite, migraciones y datos sembrados, gastos fijos, importación, proveedor de inversiones | — |
| Datos | SQLite (`finanzas.db`) | Un archivo por ambiente | — |

El dominio no depende de nada; la API arma los datos con EF Core y llama a funciones puras del dominio. Eso permite
probar las reglas sin base de datos.

## Flujo: cargar un movimiento con tarjeta

```mermaid
sequenceDiagram
  actor U as Usuario
  participant App as App mobile
  participant API as POST /transactions
  participant Dom as Domain
  participant DB as SQLite
  U->>App: Completa el formulario (tarjeta, 3 cuotas)
  App->>API: NewTransactionDto (pesos)
  API->>DB: Lee medio de pago con cierres y tipo de operación
  API->>API: Valida tipo, ámbito y medio
  API->>Dom: CardCalendar.ProposeDueDate(cierres, fecha)
  Dom-->>API: Primer vencimiento (o 400 si falta el cierre)
  API->>Dom: CashImpactBuilder.Build(movimiento, medio, caja del ámbito)
  Dom-->>API: 3 CashImpact, uno por vencimiento, con CardId
  API->>DB: INSERT Transactions + CashImpacts
  API-->>App: 201 TransactionDto
  App->>App: Invalida dashboard, flujo de caja, reportes y ahorro
```

El dashboard (`GET /dashboard`) recorre el camino inverso: carga movimientos con sus impactos, cajas, resúmenes,
presupuestos y gastos fijos, y `CashFlowCalculator` calcula caja al corte, fin de mes, trimestral y pagos de tarjeta.

## Despliegue (ambiente de pruebas)

Corre en la PC de Pablo con Docker Desktop; no hay nada en la nube.

```mermaid
flowchart LR
  subgraph PC["PC (Docker Desktop)"]
    subgraph Compose["docker compose"]
      A["api<br/>imagen backend/Dockerfile<br/>:8080 → 5080"]
      ADM["db-admin<br/>sqlite-web<br/>127.0.0.1:8090"]
      V[("volumen finanzas-pruebas<br/>/data/finanzas.db")]
      A --- V
      ADM --- V
    end
    EX["Expo dev server<br/>npx expo start (:8081 web)"]
    BR["Navegador<br/>localhost:8081"]
  end
  CEL["Celular con Expo Go<br/>(misma Wi-Fi)"]
  EX --> BR
  EX --> CEL
  BR -->|HTTP :5080| A
  CEL -->|HTTP IP-de-la-PC:5080| A
```

- La app toma la URL de la API de `EXPO_PUBLIC_API_URL` o, si no está, del host que sirve Expo, puerto 5080
  (`mobile/src/api/client.ts`).
- La API no tiene login y el CORS está abierto: solo debe exponerse en la red local. El admin de la base escucha solo en
  `127.0.0.1`.
- `scripts/seed-pruebas.sh` (o `.ps1` en Windows) carga la foto de octubre de la planilla en la base de pruebas.

## Reglas que implementa `Finanzas.Domain`

| Regla | Dónde |
| --- | --- |
| La compra con tarjeta va al resumen del primer cierre igual o posterior a la fecha de compra | `CardCalendar.ProposeDueDate` |
| Compra en N cuotas = un movimiento y N impactos en caja; el resto del redondeo va en la primera | `CashImpactBuilder.Build` |
| Cada movimiento tiene un tipo de operación configurable (Configuración → Tipos de operación): en qué ámbitos se ofrece, si suma o resta de caja y su comportamiento (ingreso/gasto, pago de tarjeta, transferencia, ahorro, inversión) | `OperationType`, `CashImpactBuilder.Build` |
| Las transferencias (Aporte a Memey, Retiro de Memey) mueven plata entre cajas sin cambiar el total | `CashImpactBuilder.Build` |
| Ahorro e inversión salen de la caja (o vuelven con Suma) y se ven aparte; no consumen presupuesto | `Savings.BalanceAt` (`GET /savings`) |
| Gastos fijos con medio de pago, monto vigente por mes, previstos hasta diciembre | `RecurringRuleExpander` |
| Caja al corte = saldo inicial + realizados hasta la fecha de corte (hoy por defecto) | `CashFlowCalculator.CashAt` |
| Por tarjeta y vencimiento: el mayor entre modelado, resumen real y pago previsto, menos lo pagado | `CashFlowCalculator.ProjectedAt` |
| El impacto en caja va a la caja del ámbito del movimiento (un gasto familiar pagado con Memey MP sale de la caja familiar) | `CashImpactBuilder.Build` |
| Consumo del presupuesto: movimientos de tipo ingreso/gasto que restan de caja (gastos, cuota del auto) realizados, con tarjeta el día de la compra; las cuotas anteriores a la apertura no consumen | `Budgeting.ConsumesBudget` |
| Proyección del mes: variables al ritmo diario, fijos y deuda el mayor entre presupuesto y real; alertas 70/80/100/120% | `Budgeting.Report` |
| Meses futuros: se descuenta el presupuesto no cubierto por gastos fijos cargados, el día 20 | `Budgeting.MonthlyEstimates` |
| Flujo de caja con saldo acumulado: cierra en la caja actual al corte y en la caja a fin de mes | `CashFlowCalculator.Ledger` (`GET /cashflow`) |
| Al crear o cambiar un gasto fijo (o los cierres de su tarjeta) se regeneran sus previstos; los realizados no se tocan | `RecurringRuleSync` |

## Datos

Montos en centavos (`long`), fechas `DateOnly`. Las proyecciones leen solo `CashImpact`. Cierres y vencimientos de tarjeta en `CardCycle`, uno por mes, editables desde Configuración. Detalle de tablas, relaciones y datos sembrados en [base-de-datos.md](base-de-datos.md).

## Endpoints

Montos en pesos, fechas `yyyy-MM-dd`, meses `yyyy-MM`, enums en camelCase (`familia`, `pagoTarjeta`, `realizado`). `scope` acepta `familia` o `memey`: no hay vista total (sin indicar, saldos y reportes son de Familia; los listados traen los dos ámbitos).

| Método y ruta | Para qué |
| --- | --- |
| `GET /health` | Chequeo de vida |
| `GET /openapi/v1.json` | Especificación OpenAPI generada |
| `GET /dashboard?scope&date` | Caja al corte (hoy por defecto), fin de mes, trimestral, gasto estimado de meses futuros, Memey, pagos de tarjeta por mes |
| `GET /cashflow?scope&date&until` | Flujo de caja del mes del corte con saldo acumulado: cierra en la caja actual y en la de fin de mes |
| `GET/POST /transactions`, `POST /transactions/{id}/confirm`, `DELETE /transactions/{id}` | Movimientos |
| `GET /cards/{id}/due-date?purchaseDate`, `GET/PUT /cards/{id}/cycles` | Vencimiento propuesto y cierres mes a mes |
| `GET/POST /recurring-rules`, `PUT/DELETE /recurring-rules/{id}` | Gastos fijos con monto vigente por mes |
| `GET/PUT /budgets/{month}` | Presupuesto mensual por categoría |
| `GET /reports/budget-vs-actual?month&cutoff&scope` | Presupuestado vs real con fecha de corte |
| `GET/PUT /settings`, `PUT /settings/cash-boxes/{id}` | Saldos iniciales, piso de caja y umbrales |
| `GET /people` | Personas |
| `GET/POST /operation-types?scope`, `PUT/DELETE /operation-types/{id}` | Tipos de operación. Cambiar el efecto recalcula sus movimientos |
| `GET/POST /categories?scope`, `PUT/DELETE /categories/{id}` | Categorías |
| `GET/POST /payment-methods`, `PUT/DELETE /payment-methods/{id}` | Medios de pago. Lo que está en uso no se borra: se desactiva (`active`) |
| `GET /savings?scope&date` | Saldo de ahorro e inversiones con sus movimientos y el valor de mercado si hay proveedor |
| `POST /investments/sync?scope` | Trae el valor de mercado del broker (503 mientras Invertir Online no esté conectado) |
| `POST /import/sheet?replace` | Carga una foto de la planilla (`data/import`) |

## Inversiones e Invertir Online

El saldo de inversiones es lo aportado neto (movimientos de tipo Inversión: aportes menos rescates). El valor de mercado
llega por `IInvestmentProvider` (`Finanzas.Domain/Investments.cs`): hoy está registrado `SinProveedorDeInversiones`, que no
informa nada, y la app muestra "pendiente de conectar con Invertir Online".

Para conectarlo:

1. Implementar `IInvestmentProvider` contra la API de Invertir Online (token con usuario y contraseña, después el
   portafolio de la cuenta) y devolver cada tenencia como `InvestmentPosition` con su valor en centavos.
2. Registrarlo en `Program.cs` en lugar de `SinProveedorDeInversiones`, con las credenciales como secretos de configuración
   (nunca en el repo).
3. `POST /investments/sync?scope=familia` guarda el valor del día en `InvestmentValuation`; `GET /savings` muestra el último.
