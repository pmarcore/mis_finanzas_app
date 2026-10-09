# Imagen de la API para el ambiente de pruebas. Build desde la carpeta backend/:
#   docker build -t finanzas-api backend
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish src/Finanzas.Api -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app .
# La base SQLite vive en /data para que sobreviva a reinicios y nuevas versiones (montar un volumen ahí).
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ConnectionStrings__Finanzas="Data Source=/data/finanzas.db"
VOLUME /data
EXPOSE 8080
ENTRYPOINT ["dotnet", "Finanzas.Api.dll"]
