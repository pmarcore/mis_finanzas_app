# Spec: dashboard de inversiones (IOL y Ripio)

Estado: **aprobada** (PR 17, 2026-10-21). Preguntas abiertas resueltas con lo recomendado: Ripio a mano, señales A + B + D, cartera objetivo vacía al empezar.
Pedido: Pablo, 2026-10-20 (hilo "Dashboard de inversiones IOL y Ripio")

## Objetivo

Una pantalla nueva, **Inversiones**, en formato dashboard y lo más gráfica posible. Tiene que mostrar:

- Las tenencias de Invertir Online (IOL) y de Ripio.
- El rendimiento de cada activo y de la cartera.
- Las cotizaciones actuales.
- Un resumen de oportunidades de inversión y de rotación de cartera.

## Qué existe hoy y qué cambia

- `IInvestmentProvider` (`Finanzas.Domain/Investments.cs`) es el punto de conexión con un broker. Hoy está registrado `SinProveedorDeInversiones`, que no trae nada, y `POST /investments/sync` responde 503.
- La pantalla **Ahorro e inversión** muestra lo aportado neto, es decir los movimientos de tipo Inversión. También muestra el valor de mercado si existe un `InvestmentValuation`. Las dos cosas se mantienen.
- **Cambia:** se agrega el proveedor real de IOL. Cada sincronización guarda, además del valor total en `InvestmentValuation` como hasta ahora, el detalle por activo para el dashboard y el gráfico de evolución.

## Decisiones (respuestas de Pablo, 2026-10-20)

1. **IOL por su API oficial** (`api.invertironline.com`), con el usuario y la contraseña de Pablo. La app los usa **solo para leer**: pide el token, el portafolio, el estado de cuenta y las cotizaciones. No implementa ninguna operación de compra, venta ni transferencia. Para no usar MCP, ver la nota del final.
2. **Ripio** se usa como billetera y no tiene una API para cuentas personales. Por eso **no se conecta**. Se propone cargar las tenencias de Ripio a mano. Ver la pregunta abierta 1.
3. **Moneda:** todo se muestra en pesos y en dólares. La conversión usa el **dólar MEP**.
4. **Rendimiento: el que informa IOL, sin recalcular.** Por activo se muestran exactamente los campos de IOL:
   - `gananciaDinero`
   - `gananciaPorcentaje`
   - `variacionDiaria`
   - `ppc` (precio promedio de compra)
   - `ultimoPrecio`
   - `valorizado`
5. **Actualización:** se actualiza al abrir la pantalla y también con el botón "Actualizar". La respuesta se guarda 15 minutos. El botón fuerza una consulta nueva.
6. **Ámbito: solo Familia.** El dashboard no tiene selector de ámbito. En Memey no se muestra.

## Credenciales (sin exponerlas)

- Van en un archivo **`.env` en la raíz del repo, al lado de `docker-compose.yml`**, y no en `mobile/.env`. La app móvil no las necesita: el que habla con IOL es el backend. Expo además mete en el paquete de la app las variables `EXPO_PUBLIC_*`, así que `mobile/` no es lugar para secretos.
  ```
  IOL_USERNAME=...
  IOL_PASSWORD=...
  ```
- `.env` ya está en `.gitignore`, tanto en la raíz como en `mobile/`, y nunca se sube al repo.
- `docker-compose.yml` le pasa esas variables al servicio `api` como `Iol__Username` e `Iol__Password`.
- Las credenciales no se escriben en logs ni las devuelve ningún endpoint. Si faltan, el proveedor queda "no conectado" y la app sigue funcionando como hoy.
- El token de IOL vive unos 15 minutos y tiene un refresh token. Se guarda solo en memoria, nunca en la base.
- Puede ser necesario **habilitar el acceso a la API** desde la cuenta de IOL. El README va a explicar cómo hacerlo.
- **Aviso:** la API de la app no tiene login. Cualquiera en la misma red Wi-Fi que llegue al puerto 5080 podría ver la cartera, igual que hoy puede ver los movimientos. Las credenciales de IOL no quedan expuestas, porque la API nunca las devuelve. Si en algún momento la app sale de la red de casa, hay que agregar login antes.

## Datos que se usan de IOL

| Endpoint de IOL | Para qué |
|---|---|
| `POST /token` | Obtener y renovar el token con el usuario y la contraseña |
| `GET /api/v2/portafolio/argentina` | Tenencias: símbolo, descripción, tipo (acción, CEDEAR, bono, ON, FCI, etc.), moneda, cantidad, `ppc`, `ultimoPrecio`, `variacionDiaria`, `gananciaPorcentaje`, `gananciaDinero`, `valorizado` |
| `GET /api/v2/portafolio/estados_Unidos` | Lo mismo para la cuenta en EE. UU., si Pablo tiene una. Si no hay tenencias, se ignora. |
| `GET /api/v2/estadocuenta` | Saldo disponible sin invertir, en pesos y en dólares |
| `GET /api/v2/bCBA/Titulos/AL30/Cotizacion` y `.../AL30D/Cotizacion` | Dólar MEP = precio de AL30 en pesos ÷ precio de AL30D en dólares. Sale de la misma fuente y sin servicios externos. |
| `GET /api/v2/{mercado}/Titulos/{simbolo}/Cotizacion/seriehistorica/{desde}/{hasta}/ajustada` | Gráfico de precio de un activo en su detalle. Se pide al abrir el detalle y no se guarda. |

**Totales.** IOL informa el rendimiento por activo, no el de la cartera entera. El total de la cartera se arma así:

- **Valor total** = suma de los `valorizado`.
- **Ganancia total en $** = suma de los `gananciaDinero`.
- **Ganancia total en %** = ganancia total en $ ÷ (valor total − ganancia total en $). El divisor es el costo que surge de los datos de IOL.

Son sumas de lo que informa IOL, no un cálculo propio de precios. La pantalla lo aclara con la leyenda "Total: suma de lo informado por IOL".

**Pesos y dólares.** El valor de cada activo se convierte a la otra moneda con el MEP del momento. El rendimiento por activo se muestra en la moneda en que lo informa IOL, sin convertirlo.

## Ripio (carga manual, propuesta)

- En Configuración → **Tenencias manuales** se carga una lista con activo (BTC, ETH, USDT, etc.), cantidad y, de forma opcional, el costo total en USD.
- La cotización de cada activo sale de una API pública de precios cripto **sin clave**, en USD. Su valor en pesos se calcula con el MEP.
- El rendimiento de Ripio solo aparece si se cargó el costo. Se calcula como valor menos costo y queda marcado "calculado por la app", porque Ripio no lo informa.
- Cuando Pablo compra o vende en Ripio, actualiza la cantidad a mano.

## Pantalla Inversiones

Se agrega una **pestaña nueva "Inversiones"** en la barra inferior. Además, la tarjeta "Ahorro e inversión" del Inicio muestra el valor de mercado actualizado.

De arriba hacia abajo:

1. **Encabezado:** "Datos de IOL al 20/10 16:45", botón Actualizar y selector **$ / US$** que cambia todos los montos de la pantalla.
2. **Indicadores principales:** cuatro tarjetas.
   - Valor total.
   - Ganancia total en $ y en %, en verde o en rojo.
   - Variación del día.
   - Disponible sin invertir.
3. **Composición (gráfico de anillo):** muestra la cartera por tipo de activo: acciones, CEDEARs, bonos, ONs, FCI, cripto y efectivo. Tiene tres pestañas: **por tipo**, **por moneda** (pesos o dólares) y **por broker** (IOL o Ripio).
4. **Evolución (gráfico de línea):** el valor total día por día, desde la primera sincronización, con rangos de 1 mes, 3 meses, 1 año y todo. Al principio va a estar casi vacío: IOL no da el historial de la cartera, así que la serie se arma con una foto por día.
5. **Rendimiento por activo (barras horizontales):** el `gananciaPorcentaje` de cada activo, ordenado de mayor a menor, con barras verdes o rojas.
6. **Tenencias (tabla):** símbolo, cantidad, PPC, último precio, variación del día, ganancia en $ y en %, valorizado y % de la cartera. Al tocar una fila se abre el **detalle del activo**, con el gráfico de precio de 6 meses y una línea horizontal en el PPC.
7. **Cotizaciones:** dólar MEP, cotizaciones de los activos que tiene Pablo y una **lista de seguimiento** editable con activos que no tiene (por ejemplo, GGAL, SPY y BTC).
8. **Oportunidades y rotación:** las señales que se elijan de la sección siguiente.

Si IOL no responde, la pantalla muestra la última foto guardada con el aviso "No se pudo actualizar, datos al …".

Gráficos: `react-native-gifted-charts` sobre `react-native-svg`. Funciona en el teléfono y en Expo web, que es como Pablo prueba en la PC.

## Oportunidades y rotación: propuestas

Todas son **análisis informativos** hechos con los datos que muestra la app. **No son recomendaciones de compra o venta ni asesoramiento financiero**, y la sección lo dice en su encabezado. Cada señal es una tarjeta corta: qué se detectó, con qué números y qué se podría revisar.

### A. Concentración
Marca un activo que pesa más del 25% de la cartera, o un tipo de activo o una moneda que pesa más del 60%. Por ejemplo: "GGAL es el 32% de tu cartera". Los umbrales se pueden editar. No necesita configuración previa.

### B. Desvío contra una cartera objetivo (rebalanceo)
Pablo define una vez la cartera que quiere por tipo de activo. Por ejemplo: 40% renta fija en dólares, 30% CEDEARs, 15% acciones, 10% cripto y 5% efectivo. La señal muestra el desvío de cada tipo y **cuánta plata habría que mover** para volver a esa cartera. Por ejemplo: "CEDEARs 38% (objetivo 30%): +$1.200.000 por encima; renta fija 31% (objetivo 40%): −$1.350.000". Solo se marcan los desvíos mayores a 5 puntos. Es la señal que más se parece a "rotar la cartera" y necesita cargar el objetivo una vez.

### C. Rendimiento relativo
Usa el `gananciaPorcentaje` de IOL:

- Los 3 mejores y los 3 peores activos.
- Los activos con una pérdida mayor al 15% desde la compra, para revisar si se mantienen.
- Los activos con una ganancia mayor al 50%, que pesan cada vez más y son candidatos a revisar una toma de ganancia.

Muestra los datos sin decir qué hacer.

### D. Plata quieta
Combina dos fuentes:

- **En IOL:** saldo disponible sin invertir mayor a $100.000 o US$100.
- **En la caja Familia de la app:** si la proyección de fin de mes, que ya existe, deja un excedente por encima de un colchón configurable.

Por ejemplo: "Tenés $850.000 sin invertir en IOL" o "La caja proyectada a fin de mes supera tu colchón por $400.000". Es la que mejor aprovecha la app, porque conecta las inversiones con el flujo de caja.

### E. Movimientos fuertes
Marca los activos de la cartera o de la lista de seguimiento con una variación diaria mayor a ±5%. Es informativa y del día.

### Recomendación

Para la primera versión recomiendo **A + B + D**:

- **A** y **D** funcionan desde el primer día sin cargar nada.
- **B** es la que responde a "rotación de cartera", con un objetivo explícito y montos concretos.

**C** y **E** son baratas de agregar y pueden entrar si Pablo quiere. Todas leen los umbrales de **Configuración → Inversiones**: objetivo por tipo, umbrales y colchón de caja.

## Modelo de datos (una migración: `DashboardInversiones`)

| Tabla | Campos | Para qué |
|---|---|---|
| `InvestmentSnapshots` | Date, Source (`invertirOnline` / `ripio`), Symbol, Description, AssetType, Currency, Quantity, LastPrice, AvgCost, DailyChangePct, GainPct, GainAmount, MarketValueArs, MarketValueUsd, GainIsComputed, TakenAt | Una foto por activo y por día. Cada sincronización del mismo día **reemplaza** la foto de ese día, con clave única (Date, Source, Symbol). El efectivo disponible se guarda como una fila con AssetType `efectivo`. |
| `ManualHoldings` | Source, Symbol, Quantity, CostUsd (opcional), Active | Tenencias de Ripio cargadas a mano |
| `InvestmentTargets` | AssetType, TargetPct | Cartera objetivo (señal B) |
| `InvestmentSettings` | Umbrales de A, C, D y E, colchón de caja, lista de seguimiento | Configuración de las señales |
| `InvestmentValuations` (ya existe) | sin cambios | Cada sincronización sigue guardando el total en pesos, así Ahorro e inversión e Inicio siguen funcionando |

Los montos se guardan en centavos (`long`), como en el resto de la app. Los precios y porcentajes se guardan como `decimal`.

## API

| Endpoint | Qué hace |
|---|---|
| `GET /investments/dashboard?refresh=false` | Devuelve todo el dashboard: fecha de los datos, MEP, totales, composición, tenencias, cotizaciones y señales. Usa lo guardado de los últimos 15 minutos, salvo con `refresh=true`. |
| `POST /investments/sync` | Ya existe y ahora funciona: sincroniza IOL y los precios de Ripio, guarda la foto del día y el `InvestmentValuation`. |
| `GET /investments/history?from&to` | Serie diaria del valor total en pesos y en dólares, para el gráfico de evolución |
| `GET /investments/assets/{symbol}/history?months=6` | Serie de precios de un activo, tomada de IOL, para el detalle |
| `GET/POST/PUT/DELETE /investments/manual` | Tenencias manuales de Ripio |
| `GET/PUT /investments/targets` | Cartera objetivo |
| `GET/PUT /investments/settings` | Umbrales, colchón y lista de seguimiento |

## Backend

- `IolInvestmentProvider` implementa `IInvestmentProvider` con `HttpClient`. Maneja el token y su renovación en memoria. Solo hace llamadas de lectura.
- Se amplía `InvestmentPosition` con los campos de IOL: tipo, moneda, PPC, último precio, variaciones y ganancias. Se registra en `Program.cs` si hay credenciales. Si no las hay, sigue `SinProveedorDeInversiones`.
- `ManualHoldingsProvider` arma las tenencias de Ripio con precios de la API pública.
- `InvestmentSignals`, en Dominio, calcula las señales A a E. Son funciones puras y testeables.
- La caché de 15 minutos usa `IMemoryCache`.

## Pruebas

- Lectura de respuestas de IOL con JSON de ejemplo y un `HttpMessageHandler` falso. En CI no se llama a IOL.
- Cálculo del MEP, totales y conversión entre pesos y dólares.
- Reemplazo de la foto del mismo día.
- Cada señal con casos que la disparan y que no la disparan.
- Proveedor sin credenciales: la app se comporta como hoy.

## Documentación

Se actualizan `docs/arquitectura.md` (sección Inversiones), `docs/base-de-datos.md`, `docs/clases-backend.md`, `docs/app-mobile.md`, `docs/tecnologias.md` (librería de gráficos) y el README (cómo cargar las credenciales y habilitar la API en IOL).

## Fuera de alcance

- Comprar, vender u operar desde la app.
- Conectar Ripio por API.
- Inversiones de Memey.
- Impuestos.
- Cualquier recomendación personalizada de inversión.

## Preguntas abiertas

1. **Ripio:** ¿se carga a mano (recomendado, porque así aparece en el dashboard) o se deja afuera por ahora?
2. **Señales:** ¿A + B + D (recomendado), o también C y E?
3. **Cartera objetivo (B):** ¿ya tenés una distribución en mente? Si no, se arranca vacía y la cargás en Configuración.

## Nota: API y no MCP

Un MCP sirve para que Claude lea datos durante una conversación. La pantalla la arma el backend, que tiene que consultar IOL por su cuenta, así que necesita la API. Si más adelante Pablo quiere analizar la cartera en el chat, se puede sumar un MCP aparte sin cambiar este diseño.

## Ajustes al construir (PR de la feature)

- Gráficos hechos con `react-native-svg` directo (anillo, línea y barras) en lugar de `react-native-gifted-charts`: es una
  dependencia menos y anda igual en el teléfono y en Expo web.
- El colchón de la señal D es el **piso de caja** que ya existe en Configuración, para no tener dos valores parecidos.
- La API pública de precios cripto es la de Binance (sin clave, contra USDT).
- En la cartera objetivo, un tipo de activo que tenés pero no está en el objetivo cuenta como objetivo 0 %.
