FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 8083
ENV ASPNETCORE_URLS=http://+:8083

FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["src/Candidate.Portal/Candidate.Portal.csproj", "src/Candidate.Portal/"]
RUN dotnet restore "src/Candidate.Portal/Candidate.Portal.csproj"
COPY . .
WORKDIR "/src/src/Candidate.Portal"
RUN dotnet build "Candidate.Portal.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "Candidate.Portal.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Candidate.Portal.dll"]

