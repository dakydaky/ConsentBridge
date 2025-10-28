# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY src/MockBoard.Adapter/MockBoard.Adapter.csproj ./MockBoard.Adapter/
COPY src/Gateway.Domain/Gateway.Domain.csproj ./Gateway.Domain/
RUN dotnet restore ./MockBoard.Adapter/MockBoard.Adapter.csproj

COPY src/MockBoard.Adapter/ ./MockBoard.Adapter/
COPY src/Gateway.Domain/ ./Gateway.Domain/
RUN dotnet publish ./MockBoard.Adapter/MockBoard.Adapter.csproj -c Release -o /app/publish

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8081
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "MockBoard.Adapter.dll"]
