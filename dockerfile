FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore Colibri.Api/Colibri.Api.csproj
RUN dotnet publish Colibri.Api/Colibri.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app .
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser
ENTRYPOINT ["dotnet", "Colibri.Api.dll"]