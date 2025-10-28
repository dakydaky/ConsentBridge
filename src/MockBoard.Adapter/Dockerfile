# build
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ../../src/MockBoard.Adapter/MockBoard.Adapter.csproj ./
RUN dotnet restore
COPY ../../src/MockBoard.Adapter/ .
RUN dotnet publish -c Release -o /app

# run
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8081
COPY --from=build /app .
ENTRYPOINT ["dotnet", "MockBoard.Adapter.dll"]
