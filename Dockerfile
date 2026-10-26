# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY global.json EcommerceAPI.csproj ./
RUN dotnet restore EcommerceAPI.csproj
COPY . .
RUN dotnet publish EcommerceAPI.csproj -c Release -o /app/publish --no-restore -p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

# TLS is expected to end at a reverse proxy, so trust its X-Forwarded-* headers (client IPs for rate limiting).
# The image keeps one SQLite file on a volume and applies migrations when it starts.
ENV ASPNETCORE_HTTP_PORTS=8080 \
    ASPNETCORE_FORWARDEDHEADERS_ENABLED=true \
    ConnectionStrings__DefaultConnection="Data Source=/data/ecommerce.db" \
    Database__ApplyMigrationsOnStartup=true

RUN mkdir /data && chown $APP_UID:$APP_UID /data
VOLUME /data

COPY --from=build /app/publish .
USER $APP_UID
EXPOSE 8080
ENTRYPOINT ["dotnet", "EcommerceAPI.dll"]
