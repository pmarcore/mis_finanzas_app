# Spec: desplegar la app en los celulares

Estado: **borrador, esperando aprobación de Pablo**.
Pedido: Pablo, 2026-11-12 (hilo "Desplegar la app en los celulares")

> "¿Cómo podemos deployarla para que mi mujer y yo la tengamos instalada en el celular?"

## Objetivo

Que Pablo y su esposa tengan la app en su celular, con un ícono en la pantalla de inicio, que funcione desde cualquier lugar (no solo en el Wi-Fi de casa) y que los dos vean y carguen **los mismos datos**. Con el menor costo y la menor cantidad de cosas para mantener.

## Cómo está hoy

- **API** .NET 10 + SQLite en un contenedor Docker en la PC de Pablo (`docker compose`, puerto 5080, volumen `finanzas-pruebas`). Ya tiene `backend/Dockerfile` y la base vive en `/data`.
- **App** Expo React Native. Hoy se usa con Expo Go en el mismo Wi-Fi o como web (`npx expo start --web`, PR 7). La URL de la API sale del host de Expo, o de `EXPO_PUBLIC_API_URL`.
- **No hay login.** Cualquiera que llegue a la API puede leer y modificar todo. CORS abierto. En la red de casa no importa; en internet sí.
- Las credenciales de IOL van por variables de entorno (`.env`).
- Hay un admin web de la base (`db-admin`, puerto 8090) solo para la PC.

Para usarla en los celulares hay que resolver tres cosas: **dónde corre la API** (que esté prendida siempre y accesible), **cómo se protege** y **cómo se instala la app**. Compartir los datos sale solo: si los dos celulares hablan con la misma API, ven la misma base.

## 1. Dónde corre la API

| Opción | Costo aprox. | A favor | En contra |
|---|---|---|---|
| **A. Fly.io** (recomendada) | ~US$3 a 5/mes (máquina chica de 256 MB + disco de 1 GB, región São Paulo) | Usa el `Dockerfile` que ya existe. HTTPS automático con dominio `*.fly.dev`. Disco persistente para SQLite con snapshots diarios. Deploy automático desde GitHub. La PC puede estar apagada. | Hay que crear cuenta con tarjeta. Los datos salen de casa (quedan en un servidor de Fly, cifrados en tránsito). |
| B. PC de casa + Tailscale | Gratis | Los datos no salen de casa. Tailscale arma una red privada entre la PC y los dos celulares, con HTTPS (`tailscale serve`), sin abrir puertos del router. | La PC tiene que estar prendida 24/7 con Docker Desktop corriendo. Los dos celulares necesitan la app de Tailscale activa. Si se corta la luz o internet en casa, no hay app. |
| C. VPS (Hetzner, DigitalOcean) | ~US$4 a 6/mes | Control total, precio fijo. | Hay que administrar el servidor: actualizaciones del sistema, firewall, certificado HTTPS (Caddy). Más mantenimiento que Fly por un precio parecido. |
| D. Azure App Service | Gratis (F1) a ~US$13/mes (B1) | Conocido en el mundo .NET. | El plan gratis se duerme y tiene límites de CPU; SQLite sobre el disco compartido de App Service anda lento y con bloqueos. Para que ande bien hay que pagar B1 o cambiar a otra base. Lo más caro para este uso. |

**Recomendación: A, Fly.io.** Es la más barata entre las que no dependen de la PC, no hay servidor que mantener y el contenedor ya está hecho. Si Pablo prefiere que los datos no salgan de casa y no le molesta dejar la PC prendida, B es gratis y también sirve: el resto de esta spec (login, PWA) aplica igual.

Detalles de la opción A:

- Una sola máquina en la región `gru` (São Paulo, la más cercana a Argentina). Desde ahí también responde la API pública de precios de cripto (Binance bloquea pedidos desde EE.UU.).
- Un volumen de 1 GB montado en `/data` (la base hoy pesa unos pocos MB).
- Credenciales de IOL y la clave para firmar los logins como *secrets* de Fly, nunca en el repo.
- `fly.toml` en el repo y un workflow de GitHub Actions que, cuando se mergea a `main` y pasan los tests, despliega solo. Pablo agrega una vez el token de Fly como secret del repo.
- La máquina puede apagarse sola cuando nadie la usa y prenderse con el primer pedido (tarda 1 o 2 segundos). Baja el costo; si molesta la demora, se deja siempre prendida.

## 2. Persistencia y backups de SQLite

1. **La base vive en el volumen** (`/data/finanzas.db`), igual que hoy en Docker. Sobrevive a reinicios y a nuevas versiones; las migraciones corren solas al arrancar, como ahora.
2. **Snapshots diarios del volumen** que hace Fly automáticamente (se guardan 5 días por defecto). Cubren que se rompa el disco.
3. **Copia diaria propia:** la API hace todas las noches `VACUUM INTO /data/backups/finanzas-AAAA-MM-DD.db` y guarda las últimas 30. Cubre el error humano ("borré algo hace dos semanas").
4. **Botón "Descargar backup"** en Configuración, que baja una copia de la base al momento. Sirve para tener una copia en la PC y para abrirla con DB Browser for SQLite.
5. El admin web de la base (`db-admin`) **no se publica** en internet. Para mirar la base en la nube se descarga un backup, o se usa `fly proxy` desde la PC.

Opcional, si después se quiere más resguardo: replicar la base en tiempo real a un almacenamiento externo con Litestream (Cloudflare R2 o Tigris tienen capa gratis). No lo incluyo en la primera versión.

## 3. HTTPS

- Fly da HTTPS automático en `https://<nombre>.fly.dev`. Si más adelante se quiere un dominio propio (ej. `finanzas.algo.com.ar`), se agrega con certificado gratis; no hace falta para arrancar.
- La API redirige HTTP a HTTPS y CORS se cierra al propio dominio (la app web y la API van en el mismo dominio, ver punto 5).

## 4. Autenticación

Hoy no hay login y en internet eso no puede quedar así. Opciones:

| Opción | Esfuerzo | A favor | En contra |
|---|---|---|---|
| Una clave compartida (API key) metida en la app | Bajo | Rapidísimo. | La clave queda dentro de la app: si se pierde un celular hay que cambiarla y reinstalar en los dos. No se sabe quién hizo cada cosa. |
| **Login simple, un usuario por persona** (recomendada) | Medio | Cada uno entra con su usuario y contraseña una sola vez y queda logueado. Si se pierde un celular se cierra esa sesión sin tocar el otro. Permite registrar quién cargó cada movimiento. | Un poco más de trabajo: tabla de usuarios, pantalla de login. |
| Login con Google | Medio-alto | Sin contraseñas nuevas. | Configurar Google Cloud, más piezas. No vale la pena para dos personas. |

**Recomendación: login simple con dos usuarios.**

- Tabla `Users` (nombre, usuario, contraseña con hash). No hay registro abierto: los dos usuarios se crean al desplegar, con un comando o variables de entorno.
- `POST /auth/login` devuelve un token (JWT) que dura 90 días. La app lo guarda (en el celular, almacenamiento seguro; en la web, el almacenamiento del navegador) y lo manda en cada pedido. El `setAuthToken` que ya existe en `mobile/src/api` queda listo para esto.
- **Todas** las rutas piden token, salvo `/health` y `/auth/login`. Sin token: 401 y la app vuelve a la pantalla de login.
- Límite de intentos de login para frenar fuerza bruta.
- Botón "Cerrar sesión" en Configuración.
- **Los dos ven y editan todo** (Familia y Memey), porque es una sola base compartida. Opcional: guardar en cada movimiento quién lo cargó.

## 5. Instalación en el celular

| Opción | Costo | iPhone | Android | Actualizaciones |
|---|---|---|---|---|
| **PWA: la versión web instalada en la pantalla de inicio** (recomendada) | Gratis | Sí (Safari → Compartir → "Agregar a inicio") | Sí (Chrome → "Instalar app") | Automáticas: cada deploy actualiza a los dos. |
| App nativa Android (APK con EAS Build) | Gratis (capa gratis de Expo) | No | Sí, se instala un APK desde un link (hay que permitir "orígenes desconocidos") | Con EAS Update (gratis para este uso) o reinstalando. |
| App nativa iPhone (TestFlight o distribución interna) | **US$99 por año** (cuenta de Apple Developer) | Sí | No | TestFlight; las builds vencen a los 90 días y hay que subir otra. |
| Expo Go como hoy | Gratis | Sí | Sí | No sirve para el día a día: necesita la PC con Expo corriendo. |

**Celulares de la casa:** Pablo tiene Android y su esposa iPhone. Por eso la opción tiene que servir a los dos.

**Recomendación: PWA.** La app ya funciona en web (PR 7), así que no hay que reescribir nada, sirve igual para iPhone y Android, no se paga la cuenta de Apple, no hay tiendas ni APKs, y cada cambio que se mergea les llega a los dos solo. Abre a pantalla completa con su ícono, como una app.

Lo que implica:

- `npx expo export --platform web` genera la web estática y **la sirve la misma API** desde el mismo contenedor (mismo dominio: sin problemas de CORS y una sola cosa para desplegar). El Dockerfile pasa a tener un paso de Node que construye la web.
- Manifiesto web (nombre "Finanzas", ícono, color, pantalla completa) para que se instale como app.
- En la web la URL de la API es la misma del sitio, sin configurar nada.
- Revisar en un celular real las pantallas: teclados numéricos, combos, gráficos. Lo que no ande bien en web se corrige.
- Limitaciones: sin internet no abre (igual que hoy, la app siempre necesita la API); no hay notificaciones push en esta versión.

Si más adelante quieren la app nativa, el código es el mismo: para Android es gratis (APK con EAS); para iPhone hace falta la cuenta de US$99/año.

## 6. Datos compartidos entre los dos celulares

Sale solo con el diseño: una API, una base, dos usuarios. Lo que carga uno lo ve el otro al refrescar (la app ya vuelve a pedir los datos al entrar a cada pantalla). No hay sincronización ni copias locales que puedan quedar desparejas.

## Costo total con la recomendación

| Concepto | Costo |
|---|---|
| Fly.io (máquina + disco + snapshots) | ~US$3 a 5 por mes |
| PWA | Gratis |
| Dominio propio | Opcional, no hace falta |
| Cuenta Apple | No hace falta |

Con la opción B (PC + Tailscale) el costo es cero.

## Qué hace Pablo (una sola vez)

1. Crear la cuenta en Fly.io con tarjeta, y crear un token para que GitHub despliegue (le paso los pasos exactos).
2. Cargar el token como secret del repo en GitHub, y las credenciales de IOL y las contraseñas de los dos usuarios como secrets de Fly.
3. Si quieren arrancar con los datos actuales: subir la base de la PC (le paso el comando).
4. En cada celular: abrir el link, loguearse y "Agregar a inicio".

## Preguntas para Pablo (con mi recomendación)

1. **¿Fly.io (~US$3 a 5/mes, datos en la nube) o PC de casa con Tailscale (gratis, la PC prendida siempre)?** Recomiendo Fly.io.
2. **¿Con qué datos arrancamos?** ¿Subo la base que tenés hoy en la PC (la de pruebas, con lo que vengas cargando) o arrancamos con una base limpia y cargás desde cero? Recomiendo subir la actual si ya tiene datos reales.
3. **¿Login con usuario y contraseña para cada uno?** Recomiendo que sí, y que los dos vean todo. ¿Querés además que cada movimiento guarde quién lo cargó?
4. **¿PWA alcanza para los dos, o querés app nativa?** Recomiendo PWA. La alternativa nativa sería APK gratis para tu Android y TestFlight para el iPhone de tu esposa, que exige la cuenta de Apple de US$99/año y renovar la build cada 90 días.

## Fuera de alcance

- Notificaciones push y funcionamiento sin internet.
- Publicar en Google Play o App Store.
- Permisos distintos por usuario (ej. que la esposa vea solo Memey).
- Réplica en tiempo real con Litestream (se puede sumar después).
- Mover la base a otro motor (Postgres, etc.): SQLite alcanza para dos usuarios.

## Pruebas

- Backend: login correcto e incorrecto, rutas sin token devuelven 401, token vencido, límite de intentos, backup diario genera el archivo y borra los de más de 30 días, descarga de backup pide token.
- CI: el build del Dockerfile con la web incluida.
- Manual: instalar la PWA en los dos celulares, loguearse, cargar un movimiento en uno y verlo en el otro.

## Documentación

- `docs/despliegue.md` nuevo: cómo está desplegado, cómo se crea un usuario, cómo restaurar un backup, cómo ver logs.
- Actualizar `docs/arquitectura.md` (diagrama de despliegue), `README.md` y `docs/desarrollo.md` (el ambiente local sigue funcionando igual, con un usuario de prueba).
