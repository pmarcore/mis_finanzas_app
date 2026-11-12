# Spec: consumos por tarjeta de crédito

Estado: **aprobada** (PR 19, 2026-11-12). Preguntas 2 a 4 resueltas con lo recomendado.
Pedido: Pablo, 2026-11-12 (hilo "Consumos por tarjeta de crédito")

> "Quiero ver el detalle de consumos realizados por cada tarjeta de crédito y también una vista unificada. También debo poder ver los gastos fijos que tengo con las tarjetas en un filtro aparte."

## Objetivo

Ver qué compone cada resumen de tarjeta: compra por compra, con sus cuotas, para una tarjeta o para todas juntas, y poder quedarse solo con los gastos fijos cargados con tarjeta.

## Qué existe hoy y qué cambia

- Una compra con tarjeta genera una **línea de resumen por cuota** (`CashImpact` con `CardId`), con fecha igual al vencimiento de esa cuota. El primer vencimiento sale de los cierres y vencimientos cargados mes a mes (`CardCycle`).
- El total de cada resumen ya se calcula (`CashFlowCalculator.CardStatements`): por tarjeta y vencimiento manda el mayor entre lo modelado, el resumen real confirmado (`Statement.ConfirmedAmount`) y el pago previsto, menos lo ya pagado. Es lo que muestra la card **Pagos de tarjeta** del inicio.
- La pantalla **Tarjetas** (se abre con "Ver" en esa card) hoy solo muestra los cierres y vencimientos.
- Movimientos ya filtra por medio de pago, pero por **fecha de compra**, no por resumen.
- Los gastos fijos generan movimientos previstos con `RecurringRuleId`; al confirmarlos lo conservan.
- **Cambia:** la pantalla Tarjetas pasa a mostrar el detalle de cada resumen, con vista unificada y filtro de fijos. No hay migración: todo sale de tablas que ya existen.

## Reglas

1. **El detalle es por resumen, es decir por mes de vencimiento.** El "resumen de diciembre" de una tarjeta son todas sus líneas que vencen en diciembre: compras nuevas, cuotas de compras anteriores y fijos. Así el total coincide con lo que se paga y con la card Pagos de tarjeta.
2. **Una línea por cuota.** Una compra de $60.000 en 6 cuotas aparece en seis resúmenes, con $10.000 y "cuota 2/6", etc. Si hay resto por redondeo va en la primera cuota, como hoy.
3. **Entra todo lo que tiene línea de resumen:** realizados y previstos (los fijos futuros son previstos), compromisos anteriores a la apertura (marcados "compra anterior") y reintegros (suman, en verde).
4. **Vista unificada = todas las tarjetas del mismo mes de vencimiento**, agrupadas por tarjeta con su subtotal y su fecha de vencimiento, más el total del mes.
5. **Siempre por entidad, Familia o Memey**, con el selector de ámbito de siempre. Nunca un total de las dos entidades.
6. **Filtro de fijos:** "Todos / Solo fijos / Sin fijos". "Solo fijos" deja las líneas de movimientos con `RecurringRuleId`. Los totales de cabecera del resumen no cambian con el filtro; lo que cambia es la lista y aparece el subtotal filtrado ("Fijos: $X de $Y").
7. **Cabecera de cada resumen:** vencimiento, modelado (suma de líneas), resumen real confirmado si lo hay, pagado y pendiente. Son los mismos números de `CardStatements`; no hay un segundo cálculo.

## API

| Endpoint | Qué hace |
|---|---|
| `GET /cards/statements/months?scope` | Lista los meses de vencimiento que tienen líneas o pagos para la entidad, con el total de cada uno. Alimenta el combo de meses. |
| `GET /cards/statements/{dueMonth}?scope&cardId&recurring` | Detalle del mes (`AAAA-MM`). Sin `cardId` es la vista unificada. `recurring`: vacío = todos, `true` = solo fijos, `false` = sin fijos. |

Respuesta del detalle:

```json
{
  "dueMonth": "2026-12",
  "total": 812345.00,
  "filteredTotal": 210000.00,
  "cards": [
    {
      "cardId": 3, "card": "Visa Galicia", "dueDate": "2026-12-05",
      "modeled": 512345.00, "confirmed": null, "paid": 0, "outstanding": 512345.00,
      "filteredTotal": 150000.00,
      "lines": [
        { "transactionId": 120, "purchaseDate": "2026-10-28", "description": "Supermercado",
          "categoryId": 8, "personId": 1, "amount": -45000.00,
          "installment": 1, "installments": 1, "status": "realizado",
          "recurringRuleId": null, "isPriorCommitment": false }
      ]
    }
  ]
}
```

Montos en pesos, igual que el resto de la API. Devuelve 400 si el mes no es válido y 404 si `cardId` no es una tarjeta.

## App

**Pantalla Tarjetas (se reorganiza, se sigue abriendo desde "Ver" en Pagos de tarjeta)**

- Arriba: selector de ámbito, combo **Tarjeta** ("Todas las tarjetas" + cada tarjeta), combo **Resumen** (mes de vencimiento, con el total al lado) y segmentado **Todos / Solo fijos / Sin fijos**.
- El resumen por defecto es el próximo vencimiento a partir de hoy.
- Por cada tarjeta, una card con la cabecera (vence, modelado, real, pagado, pendiente) y la lista de líneas: fecha de compra, descripción, categoría, "cuota n/N", monto, marca **Fijo** o **Previsto** cuando corresponda.
- Tocar una línea abre **Editar movimiento**, como en Movimientos.
- Al final, la card actual de **Cierres y vencimientos** de la tarjeta elegida (con "Todas" se oculta).
- Los combos son desplegables, según la preferencia de Pablo.

**Movimientos**

- Se agrega la opción "Solo gastos fijos" a los filtros, para ver los fijos por fecha de compra con cualquier medio de pago (ver pregunta 3). Necesita un parámetro `recurring` en `GET /transactions`.

## Preguntas para Pablo (con mi recomendación)

1. **¿Detalle por resumen (mes de vencimiento) o por mes de compra?** **Resuelta: por resumen** (Pablo, 2026-11-12). Por mes de compra ya se puede ver en Movimientos filtrando por medio de pago.
2. **¿Una pantalla nueva o la de Tarjetas?** Recomiendo **ampliar Tarjetas**, que ya está enlazada desde el inicio, en lugar de sumar otra pantalla.
3. **¿Agrego también "Solo gastos fijos" en Movimientos?** Recomiendo **sí**: es un cambio chico y sirve para ver todos los fijos, no solo los de tarjeta.
4. **¿El filtro de fijos debe mostrar también la lista de gastos fijos configurados con tarjeta (aunque todavía no tengan línea en ese resumen)?** Recomiendo **no por ahora**: las líneas del resumen ya muestran los fijos de ese mes, y la lista completa está en Configuración → Fijos. Si querés, agrego un enlace "Editar fijos" en el filtro.

## Fuera de alcance

- Importar el resumen del banco (PDF o CSV) o conciliarlo línea por línea.
- Editar cierres y vencimientos desde esta pantalla (sigue en Configuración).
- Un total Familia + Memey.
- Gráficos de consumo por categoría dentro de la tarjeta.

## Pruebas (backend)

- Una compra en 6 cuotas aparece en seis meses de vencimiento con su monto y "n/6", y el resto de redondeo va en la cuota 1.
- El modelado de cada tarjeta coincide con `CardStatements` (incluido el caso con resumen confirmado mayor y con pago parcial).
- La vista unificada agrupa todas las tarjetas del mes y su total es la suma de las tarjetas.
- `recurring=true` deja solo líneas con gasto fijo y `recurring=false` las excluye; los totales de cabecera no cambian.
- Un fijo previsto futuro aparece en el resumen que le toca según los cierres cargados.
- Las líneas de Memey no aparecen en Familia y viceversa.
- `GET /cards/statements/months` lista solo meses con líneas o pagos de esa entidad.
- `GET /transactions?recurring=true` devuelve solo movimientos con gasto fijo.

## Documentación

Se actualizan `docs/arquitectura.md` (endpoints), `docs/app-mobile.md` (pantalla Tarjetas y filtro de Movimientos) y `docs/desarrollo.md` (tests).
