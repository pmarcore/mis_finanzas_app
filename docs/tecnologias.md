# Tecnologías y versiones

Versiones tomadas de los archivos del proyecto (`*.csproj`, `dotnet-tools.json`, `mobile/package.json` y
`mobile/package-lock.json`, `Dockerfile`, `docker-compose.yml`, `.github/workflows/ci.yml`). Al actualizar una
dependencia, actualizar también esta tabla.

## Backend

| Tecnología | Versión | Para qué | Dónde se declara |
| --- | --- | --- | --- |
| .NET / C# | `net10.0` (SDK 10.0) | Plataforma y lenguaje. `Nullable` e `ImplicitUsings` activados en todos los proyectos | `*.csproj` |
| ASP.NET Core (Minimal APIs) | 10.0 (SDK `Microsoft.NET.Sdk.Web`) | API HTTP | `Finanzas.Api.csproj` |
| Microsoft.AspNetCore.OpenApi | 10.0.12 | Especificación OpenAPI en `/openapi/v1.json` | `Finanzas.Api.csproj` |
| Entity Framework Core | 10.0.12 | ORM, migraciones y datos sembrados | `Finanzas.Infrastructure.csproj` |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.12 | Proveedor SQLite | `Finanzas.Infrastructure.csproj` |
| Microsoft.EntityFrameworkCore.Design | 10.0.12 | Herramientas de diseño para migraciones (solo desarrollo) | `Finanzas.Api.csproj`, `Finanzas.Infrastructure.csproj` |
| dotnet-ef | 10.0.12 | CLI de migraciones (`dotnet tool restore`) | `backend/dotnet-tools.json` |
| System.Text.Json | incluido en .NET 10 | JSON con enums en camelCase (`JsonStringEnumConverter`) | `Program.cs` |
| SQLite | la que trae el proveedor de EF Core | Base de datos en un archivo | — |

Formato de solución: `backend/Finanzas.slnx` (formato XML nuevo de soluciones).

### Tests

| Paquete | Versión | Para qué |
| --- | --- | --- |
| xunit | 2.9.3 | Framework de tests |
| xunit.runner.visualstudio | 3.1.4 | Runner para `dotnet test` |
| Microsoft.NET.Test.Sdk | 17.14.1 | Host de tests |
| Microsoft.AspNetCore.Mvc.Testing | 10.0.12 | `WebApplicationFactory<Program>`: levanta la API en memoria con una base SQLite temporal |
| Microsoft.EntityFrameworkCore.Sqlite | 10.0.12 | Base de los tests de integración |
| coverlet.collector | 6.0.4 | Cobertura de código |

## App mobile

Versión declarada en `package.json` y versión instalada según `package-lock.json`.

| Paquete | Declarada | Instalada | Para qué |
| --- | --- | --- | --- |
| expo | ~57.0.27 | 57.0.27 | SDK de Expo (SDK 57), dev server y Expo Go |
| react-native | 0.86.3 | 0.86.3 | Framework mobile |
| react | 19.2.3 | 19.2.3 | UI |
| react-dom | 19.2.3 | 19.2.3 | Render web |
| react-native-web | ~0.21.0 | 0.21.4 | Correr la app en el navegador |
| react-native-svg | 15.15.4 | 15.15.4 | Gráficos del dashboard de inversiones (anillo, línea) |
| @expo/metro-runtime | ~57.0.16 | 57.0.17 | Runtime de Metro para web |
| @react-navigation/native | ^7.5.0 | 7.5.0 | Navegación |
| @react-navigation/bottom-tabs | ^7.20.0 | 7.20.0 | Barra de tabs inferior |
| @react-navigation/native-stack | ^7.20.0 | 7.20.0 | Pantallas apiladas |
| react-native-screens | ~4.26.0 | 4.26.2 | Pantallas nativas para la navegación |
| react-native-safe-area-context | ~5.7.0 | 5.7.0 | Márgenes seguros (notch, barra de estado) |
| @tanstack/react-query | ^5.104.1 | 5.104.1 | Caché y sincronización de datos de la API |
| expo-constants | ~57.0.21 | 57.0.21 | Leer el host de Expo para detectar la URL de la API |
| expo-font | ~57.0.4 | 57.0.4 | Fuentes (íconos) |
| expo-status-bar | ~57.0.1 | 57.0.1 | Barra de estado |
| @expo/vector-icons | ^15.0.2 | 15.1.1 | Íconos (Ionicons) |
| typescript (dev) | ~6.0.3 | 6.0.3 | Tipado estático, modo `strict` |
| @types/react (dev) | ~19.2.2 | 19.2.18 | Tipos de React |

Las dependencias se agregan con `npx expo install <paquete>` para que coincidan con el SDK de Expo.

## Infraestructura y herramientas

| Herramienta | Versión | Para qué | Dónde |
| --- | --- | --- | --- |
| Docker (imagen de build) | `mcr.microsoft.com/dotnet/sdk:10.0` | Compilar y publicar la API | `backend/Dockerfile` |
| Docker (imagen de runtime) | `mcr.microsoft.com/dotnet/aspnet:10.0` | Correr la API en el puerto 8080 | `backend/Dockerfile` |
| Docker Compose | — | Ambiente de pruebas: `api` (5080) y `db-admin` (8090) con el volumen `finanzas-pruebas` | `docker-compose.yml` |
| sqlite-web | `ghcr.io/coleifer/sqlite-web:latest` | Admin web de la base, solo en `127.0.0.1:8090` | `docker-compose.yml` |
| GitHub Actions | `actions/checkout@v4`, `actions/setup-dotnet@v4` (10.0.x), `actions/setup-node@v4` (Node 22) | CI: tests del backend, `tsc` de la app y build de la imagen Docker | `.github/workflows/ci.yml` |
| Node.js | 22 (en CI) | Herramientas de Expo y TypeScript | `.github/workflows/ci.yml` |
| Bash / PowerShell | — | Cargar la foto de la planilla en la base de pruebas | `scripts/seed-pruebas.sh`, `scripts/seed-pruebas.ps1` |
