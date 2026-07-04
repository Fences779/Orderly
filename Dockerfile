FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY . .
RUN dotnet restore src/Orderly.Server/Orderly.Server.csproj
RUN dotnet publish src/Orderly.Server/Orderly.Server.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends ca-certificates curl postgresql-client tzdata \
    && rm -rf /var/lib/apt/lists/*

WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
RUN mkdir -p /opt/orderly/backups \
    && chown -R app:app /opt/orderly

USER app
ENTRYPOINT ["dotnet", "Orderly.Server.dll"]
