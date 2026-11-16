# Spec: las cuotas se atan al resumen, no a una fecha de vencimiento

Estado: **borrador, pendiente de aprobación de Pablo**.
Pedido: Pablo, 2026-11-16 (hilo "Diagnosticar cuotas atadas a vencimiento")

> "Para el mes de diciembre veo dos secciones separadas para la misma tarjeta, una con vencimiento 04/12 y otra con vencimiento 11/12. [...] las cuotas no deberían quedar atadas directamente a una fecha de vencimiento. Revisalo y dame tu diagnóstico y propuesta de solución."

## Diagnóstico

La hipótesis de Pablo es correcta y es un error de diseño, no de carga.

### Cómo funciona hoy

1. Al cargar una compra con tarjeta se calcula el **primer vencimiento** como una fecha concreta (`Transaction.FirstDueDate`), a partir de los cierres cargados (`CardCycle`).
2. `CashImpactBuilder.Build` genera una línea de resumen por cuota (`CashImpact` con `CardId`) con fecha **`FirstDueDate + i meses`**. Esa fecha queda **guardada** en la base.
3. Los resúmenes se agrupan por **(tarjeta, fecha exacta de vencimiento)** (`CashFlowCalculator.StatementSummaries`). También el pago de tarjeta (`FirstDueDate` del pago) y el resumen real confirmado (`Statement.DueDate`) se buscan por fecha exacta.
4. Al guardar los cierres de una tarjeta (`PUT /cards/{id}/cycles`) solo se regeneran los **previstos de gastos fijos**. Las compras comunes y las importadas no se tocan.

### Por qué aparecen dos resúmenes en diciembre

Verificado en la copia de la base local (`conciliacion/antes-de-carga.db`, del 12/11):

| Tarjeta | Cierre de diciembre cargado | Líneas al 04/12 | Líneas al 11/12 |
|---|---|---|---|
| Visa Santander | vence 04/12 | 10 (fijos regenerados) | 7 (cuota 2 de compras importadas) |
| Visa Galicia | vence 04/12 | 0 | 1 |
| Tarjeta Mercado Pago | vence 10/12 | 0 | 3 |

Las líneas al 11/12 son cuotas 2 de compras importadas de la planilla con primer vencimiento 11/11: `11/11 + 1 mes = 11/12`. El cierre de diciembre se corrigió a 04/12 después, los fijos se regeneraron y quedaron en 04/12, y las cuotas quedaron en 11/12. Como la agrupación es por fecha exacta, salen dos resúmenes.

Hay dos defectos combinados:

- **A. Las cuotas 2 en adelante no miran los cierres**: se calculan sumando meses al primer vencimiento. Aunque el cierre de diciembre ya estuviera bien al cargar, una compra con primer vencimiento 11/11 igual ponía su cuota 2 el 11/12.
- **B. Cambiar un cierre no mueve lo ya cargado** (salvo fijos previstos).

### El problema se va a repetir

- Las compras en cuotas cargadas en la conciliación de hoy tienen primer vencimiento 04/12, así que sus cuotas siguientes quedaron en **04/01, 04/02...**, mientras el cierre de enero cargado vence el **11/01**. En enero va a aparecer la misma división, y así cada vez que el día de vencimiento cambie de un mes a otro (lo normal en los bancos).
- Si se carga el resumen real de un mes (`Statement`) o un pago con una fecha distinta a la de las líneas, tampoco se encuentran.

## Objetivo

Que una cuota pertenezca a **un resumen (tarjeta + mes)** y que la fecha de vencimiento de ese resumen salga siempre de los cierres cargados. Corregir un cierre mueve automáticamente todas las cuotas, los fijos y los pagos de ese resumen, y nunca puede haber dos resúmenes de la misma tarjeta en el mismo mes.

## Reglas

1. **El resumen se identifica por tarjeta y mes de vencimiento** (`AAAA-MM`), no por fecha. Una tarjeta tiene a lo sumo un resumen por mes.
2. **Cada compra con tarjeta guarda el mes de su primer resumen** (nuevo `Transaction.FirstStatementMonth`). Se propone como hoy, con el primer cierre igual o posterior a la fecha de compra, y se puede cambiar a mano.
3. **La cuota *i* va al resumen del mes `primer resumen + (i-1)`.** Cada línea de resumen guarda ese mes (nuevo `CashImpact.StatementMonth`).
4. **La fecha de vencimiento de un resumen sale del cierre cargado para ese mes.** Si todavía no hay cierre cargado para ese mes (cuotas lejanas), se usa el mismo día del último vencimiento cargado de esa tarjeta, y se ajusta solo cuando se carga el cierre.
5. **Al guardar los cierres de una tarjeta se actualiza la fecha de todas sus líneas de resumen** según la regla 4 (compras, importadas, compromisos previos, fijos realizados y previstos). El mes de cada línea no cambia. La fecha guardada en `CashImpact.ImpactDate` queda como copia de la fecha del resumen, para que caja al corte, flujo de caja y proyecciones sigan funcionando igual.
6. **Cambiar un cierre no cambia a qué resumen pertenece una compra ya cargada.** Si se corrige la fecha de cierre y una compra queda del otro lado, se corrige editando esa compra (combo "Primer resumen"). Es lo que pasa en la realidad: el banco ya la asignó a un resumen.
7. **El pago de tarjeta y el resumen real confirmado también se atan al mes**: el pago guarda la tarjeta y el mes del resumen que paga; el resumen confirmado (`Statement`) se identifica por tarjeta y mes. La fecha del pago sigue siendo la fecha en que sale la plata.
8. **Los gastos fijos con tarjeta** siguen generando un previsto por mes; su primer resumen se propone con la regla 2 y se regeneran como hoy.

## Cambios

### Backend

- `Transaction`: nuevo `FirstStatementMonth` (primer día del mes). `FirstDueDate` deja de ser la fuente de verdad: en compras se devuelve calculado (vencimiento del primer resumen) para no romper la app; en pagos de tarjeta se reemplaza por el mes que paga.
- `CashImpact`: nuevo `StatementMonth` en líneas de tarjeta.
- `Statement`: el índice único pasa a (tarjeta, mes).
- `CardCalendar`: `ProposeStatementMonth(cycles, purchaseDate)` y `DueDateFor(cycles, month)` (regla 4).
- `CashImpactBuilder.Build`: arma las cuotas por mes (regla 3) y les pone la fecha con `DueDateFor`.
- `CashFlowCalculator.StatementSummaries`: agrupa por (tarjeta, mes); la fecha que muestra es la del cierre de ese mes.
- `PUT /cards/{id}/cycles`: además de regenerar fijos, actualiza `ImpactDate` de todas las líneas de esa tarjeta (regla 5).
- API de movimientos: acepta `firstStatementMonth` (`"2026-12"`); si llega solo `firstDueDate` (app vieja, script de conciliación), se toma su mes. `GET /cards/{id}/due-date` devuelve mes y fecha.
- `GET /cards/statements/{dueMonth}` deja de poder devolver dos resúmenes de la misma tarjeta en un mes.

### App

- Formulario de movimiento con tarjeta: el campo de vencimiento pasa a ser un combo **"Primer resumen"** con los meses (ej. "Diciembre 2026, vence 04/12"), propuesto solo.
- Pago de tarjeta: combo **"Resumen que paga"** por mes.
- Tarjetas: un solo bloque por tarjeta y mes, con la fecha del cierre cargado.

## Corrección de los datos ya cargados

Se hace sola con una migración, sin trabajo manual de Pablo:

1. Para cada línea de tarjeta existente: `StatementMonth` = mes de su `ImpactDate` actual. Las cuotas al 11/12 quedan en el resumen de **diciembre**, igual que las de 04/12.
2. Para cada compra con tarjeta: `FirstStatementMonth` = mes de su `FirstDueDate`.
3. Para cada pago de tarjeta: mes del resumen que paga = mes de su `FirstDueDate`.
4. Se recalcula `ImpactDate` de todas las líneas con los cierres cargados (regla 4). Diciembre de Visa Santander queda en un solo resumen al 04/12 con las 17 líneas; Visa Galicia al 04/12 y Mercado Pago al 10/12. Las cuotas de enero de las compras cargadas hoy pasan de 04/01 a 11/01.
5. `Statement`: se pasa a (tarjeta, mes). Si hubiera dos del mismo mes, se queda el de mayor monto confirmado y se informa en el log.

Se prueba primero contra una copia de la base local de Pablo y se compara el total por tarjeta y mes antes y después: **los totales mensuales no cambian**, solo se unifican los resúmenes y se corrigen las fechas. La caja proyectada sí puede moverse unos días dentro del mes (ej. lo que vencía el 11/12 ahora sale el 04/12), que es lo correcto.

La base de Fly todavía no está creada, así que la migración corre en la local y la nube arranca ya con el modelo nuevo.

## Pruebas

- Compra en 3 cuotas con cierres de vencimiento distinto cada mes (04/12, 11/01, 09/02): cada cuota cae en la fecha de su mes.
- Cambiar el vencimiento de diciembre de 11/12 a 04/12 mueve compras, importadas y fijos; queda un solo resumen.
- Cuota en un mes sin cierre cargado: usa el día del último vencimiento; al cargar el cierre se corrige.
- Pago y resumen confirmado se encuentran por mes aunque la fecha difiera.
- Migración sobre una base con el caso real (cuotas 11/12 y fijos 04/12): un solo resumen, mismo total.
- Las pruebas actuales (65) siguen pasando.

## Fuera de alcance

- Reasignar automáticamente compras a otro resumen cuando se corrige una fecha de cierre (regla 6). Si se quiere, se puede sumar después como opción.

## Preguntas para Pablo

1. **Regla 6:** ¿está bien que corregir un cierre solo mueva fechas y no cambie a qué resumen pertenece una compra ya cargada? Recomendado: sí.
2. **Regla 4:** para cuotas en meses sin cierre cargado, ¿usar el día del último vencimiento cargado? (Alternativa: un día fijo por tarjeta en Medios de pago.) Recomendado: último vencimiento cargado.
