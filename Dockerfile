# Build đa kiến trúc: chạy được cả NAS x86_64 lẫn ARM.
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY src/StoreChecking.Api/StoreChecking.Api.csproj src/StoreChecking.Api/
RUN dotnet restore src/StoreChecking.Api/StoreChecking.Api.csproj
COPY . .
RUN dotnet publish src/StoreChecking.Api/StoreChecking.Api.csproj -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=build /app .

# Kestrel nghe cổng 8080 trong container; docker-compose ánh xạ ra ngoài.
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "StoreChecking.Api.dll"]
