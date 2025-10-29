FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8082
ENV ASPNETCORE_URLS=http://+:8082

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/MockAgent.Console/MockAgent.Console.csproj", "src/MockAgent.Console/"]
RUN dotnet restore "src/MockAgent.Console/MockAgent.Console.csproj"
COPY . .
WORKDIR "/src/src/MockAgent.Console"
RUN dotnet build "MockAgent.Console.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "MockAgent.Console.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "MockAgent.Console.dll"]

