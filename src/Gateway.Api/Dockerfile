# Build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ../../global.json ./
COPY ../../src/Gateway.Domain/Gateway.Domain.csproj src/Gateway.Domain/
COPY ../../src/Gateway.Infrastructure/Gateway.Infrastructure.csproj src/Gateway.Infrastructure/
COPY ../../src/Gateway.Api/Gateway.Api.csproj src/Gateway.Api/
RUN dotnet restore src/Gateway.Api/Gateway.Api.csproj
COPY ../../src/ ./src/
RUN dotnet publish src/Gateway.Api/Gateway.Api.csproj -c Release -o /app

# Run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080
COPY --from=build /app .
ENTRYPOINT ["dotnet", "Gateway.Api.dll"]
