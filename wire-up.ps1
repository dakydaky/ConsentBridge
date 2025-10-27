param(
  [string]$SolutionName = "ConsentBridge"
)

Write-Host ">>> Wiring projects into $SolutionName.sln"

# Add to solution
dotnet sln "$SolutionName.sln" add `
  src/Gateway.Api/Gateway.Api.csproj `
  src/Gateway.Domain/Gateway.Domain.csproj `
  src/Gateway.Application/Gateway.Application.csproj `
  src/Gateway.Infrastructure/Gateway.Infrastructure.csproj `
  src/Gateway.CertAuthority/Gateway.CertAuthority.csproj `
  src/Gateway.Sdk.DotNet/Gateway.Sdk.DotNet.csproj `
  src/Gateway.Test/Gateway.Test.csproj `
  src/MockBoard.Adapter/MockBoard.Adapter.csproj

# Project references
dotnet add src/Gateway.Api/Gateway.Api.csproj reference `
  src/Gateway.Application/Gateway.Application.csproj `
  src/Gateway.Infrastructure/Gateway.Infrastructure.csproj `
  src/Gateway.Domain/Gateway.Domain.csproj

dotnet add src/Gateway.Application/Gateway.Application.csproj reference `
  src/Gateway.Domain/Gateway.Domain.csproj

dotnet add src/Gateway.Infrastructure/Gateway.Infrastructure.csproj reference `
  src/Gateway.Domain/Gateway.Domain.csproj

dotnet add src/Gateway.CertAuthority/Gateway.CertAuthority.csproj reference `
  src/Gateway.Application/Gateway.Application.csproj `
  src/Gateway.Domain/Gateway.Domain.csproj

dotnet add src/Gateway.Sdk.DotNet/Gateway.Sdk.DotNet.csproj reference `
  src/Gateway.Domain/Gateway.Domain.csproj

# Packages
dotnet add src/Gateway.Api/Gateway.Api.csproj package Swashbuckle.AspNetCore
dotnet add src/Gateway.Api/Gateway.Api.csproj package Serilog.AspNetCore
dotnet add src/Gateway.Api/Gateway.Api.csproj package Serilog.Sinks.Console

dotnet add src/Gateway.Infrastructure/Gateway.Infrastructure.csproj package Microsoft.EntityFrameworkCore
dotnet add src/Gateway.Infrastructure/Gateway.Infrastructure.csproj package Npgsql.EntityFrameworkCore.PostgreSQL
dotnet add src/Gateway.Infrastructure/Gateway.Infrastructure.csproj package Microsoft.EntityFrameworkCore.Design

dotnet add src/Gateway.Application/Gateway.Application.csproj package Microsoft.Extensions.Http

dotnet add src/Gateway.Test/Gateway.Test.csproj package FluentAssertions

dotnet add src/MockBoard.Adapter/MockBoard.Adapter.csproj package Serilog.AspNetCore
dotnet add src/MockBoard.Adapter/MockBoard.Adapter.csproj package Serilog.Sinks.Console

# Restore & build
dotnet restore
dotnet build -c Debug

Write-Host ">>> Done. Solution updated: $(Join-Path (Get-Location) "$SolutionName.sln")"
