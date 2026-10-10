FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore floristeria-colibri/floristeria-colibri.csproj
RUN dotnet publish floristeria-colibri/floristeria-colibri.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
# Hora de Chile: DateTime.Today/Now valida fechas, arma los rangos por
# defecto de los reportes y se imprime en las etiquetas. En UTC, desde las
# 21:00 ya era "mañana". La base tiene la misma zona (sql/14).
ENV TZ=America/Santiago
EXPOSE 8080
# /app/datos es un volumen en el VPS (fotos de productos). Crearla acá con
# dueño appuser hace que el volumen nazca con ese dueño; si no, sería de
# root y la app no podría escribir.
RUN adduser --disabled-password --gecos "" appuser \
    && mkdir -p /app/datos/imagenes \
    && chown -R appuser /app
USER appuser
ENTRYPOINT ["dotnet", "floristeria-colibri.dll"]