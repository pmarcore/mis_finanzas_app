# Imagen única: API .NET + app web (PWA) exportada con Expo, servida por la misma API.
# Se construye desde la raíz del repo (necesita backend/ y mobile/):
#   docker build -f backend/Dockerfile -t finanzas-api .
# La usan docker compose (ambiente local) y Fly.io (fly.toml).

FROM node:22-bookworm-slim AS web
WORKDIR /mobile
COPY mobile/package.json mobile/package-lock.json ./
RUN npm ci --no-audit --no-fund
COPY mobile/ ./
# EXPO_OFFLINE: no consulta servicios de Expo durante el export (no hacen falta para la web).
RUN EXPO_OFFLINE=1 CI=1 npx expo export --platform web --output-dir /web

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY backend/ ./
RUN dotnet publish src/Finanzas.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
COPY --from=web /web ./wwwroot
# La base SQLite vive en /data para que sobreviva a reinicios y nuevas versiones (montar un volumen ahí).
# Las copias diarias quedan en /data/backups. TZ: "hoy" es el día de Argentina aunque el servidor esté en otro lado.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Finanzas="Data Source=/data/finanzas.db" \
    TZ=America/Argentina/Buenos_Aires
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Finanzas.Api.dll"]
