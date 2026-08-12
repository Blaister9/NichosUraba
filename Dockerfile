# Versiones fijas a propósito. Con la etiqueta flotante `sdk:10.0`, un mismo commit compilaba o no
# según el día: el 2026-08-10 la etiqueta traía un SDK 10.0.3xx y el 2026-08-11 pasó a traer 10.0.400,
# que global.json —10.0.301 con rollForward latestPatch— ya no puede resolver. El build murió en
# `dotnet restore` con código 155 sin que nadie hubiera tocado el código.
#
# 10.0.301 es además el SDK con el que se ejecuta la suite en desarrollo, así que lo que se prueba y
# lo que se publica se compilan con el mismo compilador. Subir cualquiera de las dos versiones es un
# acto deliberado del proceso de publicación, no un efecto de la fecha del despliegue.
FROM mcr.microsoft.com/dotnet/sdk:10.0.301 AS build
WORKDIR /src
COPY . .
RUN dotnet restore UrabaConecta.slnx
RUN dotnet publish src/UrabaConecta.Web/UrabaConecta.Web/UrabaConecta.Web.csproj \
    --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

# El runtime se fija al último parche publicado de la banda, no al 10.0.9 que trae el SDK: .NET
# adelanta parches de forma automática y compatible, así que fijar el más nuevo mantiene los arreglos
# de seguridad sin devolver la reproducibilidad a una etiqueta que cambia sola.
FROM mcr.microsoft.com/dotnet/aspnet:10.0.11 AS runtime
USER root
RUN apt-get update && apt-get install -y --no-install-recommends curl gosu \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
COPY docker-entrypoint.sh /usr/local/bin/docker-entrypoint.sh
RUN mkdir -p /app/keys /app/media \
    && chmod 0755 /usr/local/bin/docker-entrypoint.sh
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
ENTRYPOINT ["/usr/local/bin/docker-entrypoint.sh"]
