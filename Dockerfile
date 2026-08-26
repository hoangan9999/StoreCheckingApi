# Multi-arch build: works on both x86_64 and ARM NAS hardware.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/StoreChecking.Api/StoreChecking.Api.csproj src/StoreChecking.Api/
RUN dotnet restore src/StoreChecking.Api/StoreChecking.Api.csproj
COPY . .
RUN dotnet publish src/StoreChecking.Api/StoreChecking.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .

# Which commit this image was built from. Declared AFTER the heavy layers on purpose:
# a new version must not invalidate the restore/publish cache.
ARG APP_VERSION=dev
ENV APP_VERSION=$APP_VERSION

# Kestrel listens on 8080 inside the container; docker-compose maps it outward.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "StoreChecking.Api.dll"]
