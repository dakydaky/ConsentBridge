# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY global.json ./
COPY src/Gateway.Domain/Gateway.Domain.csproj src/Gateway.Domain/
COPY src/Gateway.Infrastructure/Gateway.Infrastructure.csproj src/Gateway.Infrastructure/
COPY src/Gateway.Api/Gateway.Api.csproj src/Gateway.Api/

RUN dotnet restore src/Gateway.Api/Gateway.Api.csproj

COPY src/ ./src/

RUN dotnet publish src/Gateway.Api/Gateway.Api.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Gateway.Api.dll"]
