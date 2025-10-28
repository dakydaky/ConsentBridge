using Gateway.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Infrastructure;

public static class InfrastructureServices
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IJwsVerifier, AcceptAllVerifier>();
        return services;
    }
}
