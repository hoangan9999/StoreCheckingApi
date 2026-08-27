# Multi-arch, built by GitHub Actions for both linux/amd64 and linux/arm64.
#
# --platform=$BUILDPLATFORM pins the SDK stage to the runner's own architecture and
# cross-compiles with `-a $TARGETARCH`. Letting the SDK stage run as the target
# architecture instead would emulate the whole .NET build under QEMU, which turns a
# one-minute build into tens of minutes.
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:9.0 AS build
ARG TARGETARCH
WORKDIR /src
# All four project files first, then restore: this layer is cached until a csproj
# changes, so editing code does not re-download every NuGet package.
COPY src/StoreChecking.Domain/StoreChecking.Domain.csproj                 src/StoreChecking.Domain/
COPY src/StoreChecking.Application/StoreChecking.Application.csproj       src/StoreChecking.Application/
COPY src/StoreChecking.Infrastructure/StoreChecking.Infrastructure.csproj src/StoreChecking.Infrastructure/
COPY src/StoreChecking.Api/StoreChecking.Api.csproj                       src/StoreChecking.Api/
RUN dotnet restore -a $TARGETARCH src/StoreChecking.Api/StoreChecking.Api.csproj
COPY . .
RUN dotnet publish -a $TARGETARCH --no-restore -c Release -o /app src/StoreChecking.Api/StoreChecking.Api.csproj

# No --platform here on purpose: this stage must BE the target architecture. It only
# copies files, so nothing foreign is ever executed and no emulation is needed.
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
