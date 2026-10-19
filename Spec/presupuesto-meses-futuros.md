# Spec: presupuestar meses futuros

Estado: **borrador, pendiente de aprobación de Pablo**
Pedido: Pablo, 2026-10-19 (hilo "Presupuestar meses futuros")

## Objetivo

Armar el presupuesto de uno o varios meses futuros a partir de lo que ya está cargado: el presupuesto de un mes, el real de un mes cerrado y los gastos fijos, sin tener que tipear categoría por categoría.

## Reglas que ya existen y no cambian

- **El mes es el mes calendario, del 1.º al último día.** El presupuesto y el reporte presupuestado vs. real ya trabajan así (`Budgeting.Report`, `Budgeting.ActualTransactions`).
- Una compra con tarjeta consume en el mes de la fecha de compra. El cierre y el vencimiento de la tarjeta solo definen cuándo sale la plata de la caja. Por ejemplo, una compra del 25/10 con cierre el 23/10 consume en octubre y se paga con el vencimiento de diciembre.
- El presupuesto es por entidad, Familia o Memey, según el ámbito de la categoría. Nunca hay un total.
- Solo se presupuestan categorías de **gasto**.
- Consumen presupuesto solo los movimientos *realizados* que restan de caja. Ahorro, inversión, transferencias y pagos de tarjeta no consumen (`Budgeting.ConsumesBudget`).
- La proyección de caja de los meses futuros ya usa el presupuesto vigente de cada mes, menos lo que cubren los fijos (`Budgeting.MonthlyEstimates`). Por eso, guardar presupuestos futuros mejora la proyección sin ningún cambio extra.

## Decisiones (respuestas de Pablo, 2026-10-19: "sí a todo")

1. **Fuente.** Se elige una fuente base y, de forma opcional, la casilla "Usar gastos fijos". Las fuentes base son dos:
   - **Presupuesto de un mes**: el presupuesto vigente de ese mes, sea propio o heredado.
   - **Real de un mes cerrado**: lo consumido en ese mes.
2. **Destino.** Un rango de meses, desde y hasta. Por defecto es el mes siguiente al actual en los dos campos. Se aceptan meses desde el actual en adelante, con un máximo de 12 meses.
3. **Ajuste.** Un % opcional sobre la base, por ejemplo +5%. Se aplica una sola vez, igual para todos los meses del rango y no acumulado mes a mes. No se aplica a los fijos, porque los fijos ya tienen sus propios incrementos.
4. **Mes destino con presupuesto.** Siempre se muestra primero un borrador editable. Al guardar se reemplaza el presupuesto de la entidad en cada mes del rango, igual que hace hoy "Guardar presupuesto". En el borrador se marca qué meses ya tenían presupuesto propio.
5. **Mes cerrado.** Es un mes cuyo último día es anterior a hoy. El mes en curso no puede ser fuente "real".
6. **Redondeo.** El monto base ajustado se redondea a los $1.000 más cercanos. Los montos de los fijos quedan exactos.
7. **Herencia.** Se mantiene: un mes sin presupuesto propio usa el último mes anterior que tenga presupuesto. A partir de ahora la pantalla Presupuesto lo muestra.

## Cálculo del monto propuesto

Para cada mes destino *M* y cada categoría de gasto activa de la entidad:

1. **Base**
   - Con la fuente *presupuesto del mes X*, la base es el monto de la categoría en `BudgetsFor(X)`.
   - Con la fuente *real del mes X*, la base es la suma de `ActualTransactions` de la categoría con corte al último día de X.
2. **Base ajustada** = redondeo a $1.000 de (base × (1 + %/100)).
3. **Fijos de *M*** = la suma de `RecurringRuleExpander.AmountFor(regla, M)` de los gastos fijos de la entidad y la categoría que estén vigentes en *M*, entre `StartMonth` y `EndMonth`. Esa suma ya incluye los incrementos.
4. **Propuesto**
   - Sin la casilla "Usar gastos fijos", el propuesto es la base ajustada.
   - Con la casilla y si la categoría tiene fijos en *M*:
     - Para una categoría de tipo **Fijo** o **Deuda**, el propuesto es el monto de los fijos. Esto toma el monto vigente en *M*: si un fijo terminó o cambió de monto, se refleja.
     - Para una categoría de tipo **Variable**, el propuesto es el mayor entre la base ajustada y los fijos, para no presupuestar menos de lo que ya está comprometido.
   - Con la casilla y sin fijos en *M*, el propuesto es la base ajustada.
5. Las categorías que quedan en $0 no se guardan. Las categorías con real pero sin presupuesto en la fuente sí se proponen.

Cada fila del borrador indica de dónde salió el monto: **base**, **fijos** o **mayor entre base y fijos**.

Con la fuente *presupuesto* y sin la casilla de fijos, todos los meses del rango quedan iguales. Con fijos, cada mes puede ser distinto, por ejemplo si un fijo tiene un incremento en diciembre.

## API

| Endpoint | Qué hace |
|---|---|
| `GET /budgets/proposal?scope&source=budget\|actual&sourceMonth&from&to&useFixed&increasePct` | Devuelve el borrador sin guardar nada: por mes destino, `hasOwnBudget` y filas `{categoryId, base, fixed, proposed, origin}`. Devuelve 400 si `source=actual` y el mes no está cerrado, si `from > to`, si `from` es anterior al mes actual o si el rango supera los 12 meses. |
| `PUT /budgets?scope` | Guarda varios meses en una sola transacción. El cuerpo es `[{month, items:[{categoryId, amount}]}]`. Cada mes se reemplaza como en `PUT /budgets/{month}`. |
| `GET /budgets/{month}/source?scope` | Devuelve `{hasOwnBudget, inheritedFrom}`, donde `inheritedFrom` es el mes del que hereda, o null. Lo usa el aviso de herencia. |

`GET /budgets/{month}` y `PUT /budgets/{month}` no cambian. No hay migración, porque se usa la tabla `Budgets` actual.

## App

**Pantalla Presupuesto**
- Si el mes no tiene presupuesto propio y hereda de otro, se muestra el aviso "Este mes no tiene presupuesto propio: se usa el de octubre 2026". El borrador se precarga con esos montos y, al guardar, pasan a ser el presupuesto propio del mes.
- Hay un botón nuevo, **"Proyectar meses futuros"**, que abre la pantalla nueva con la entidad y el mes actual ya elegidos.

**Pantalla nueva "Proyectar presupuesto"**
- Tiene un selector Familia/Memey.
- Tiene un combo para la fuente: "Presupuesto de un mes" o "Real de un mes cerrado". Debajo va el mes fuente. Con "Real" solo se pueden elegir meses cerrados.
- Tiene la casilla "Usar gastos fijos", marcada por defecto.
- Tiene un campo opcional "Ajuste %".
- Tiene los campos de mes Desde y Hasta.
- El botón **"Ver borrador"** muestra el borrador de un mes por vez, con un selector para pasar de mes. Cada fila muestra categoría, monto propuesto editable y origen. Si el mes ya tenía presupuesto propio, se avisa que se va a reemplazar.
- El botón **"Guardar N meses"** confirma, guarda todo de una vez y vuelve a Presupuesto.

Los selectores usan combos desplegables, según la preferencia de UI de Pablo.

## Fuera de alcance

- Presupuestar ingresos.
- Ajustes % distintos por categoría o acumulados mes a mes.
- Copiar Familia y Memey a la vez.
- Cambiar la forma en que la proyección de caja usa el presupuesto.

## Pruebas (backend)

- La fuente presupuesto copia los montos, aplica el % y redondea a $1.000.
- La fuente real suma solo los realizados que consumen. Una compra con tarjeta cuenta en el mes de la compra aunque cierre en otro.
- La fuente real rechaza el mes en curso con 400.
- Con fijos, una categoría Fijo toma el monto vigente en cada mes destino, incluido un incremento a mitad del rango y un fijo que termina.
- Con fijos, una categoría Variable toma el mayor entre la base y los fijos.
- `PUT /budgets` reemplaza solo los meses y la entidad indicados y no toca la otra entidad.
- `GET /budgets/{month}/source` informa bien si el mes hereda.

## Documentación

Se actualizan `docs/arquitectura.md` (endpoints), `docs/app-mobile.md` (pantalla nueva y aviso) y `docs/desarrollo.md` (tests).
