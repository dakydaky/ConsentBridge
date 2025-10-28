using Gateway.Domain;
using Microsoft.Extensions.DependencyInjection;

namespace Gateway.Infrastructure;

public static class InfrastructureServices
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services)
    {
        services.AddSingleton<IJwsVerifier, AcceptAllVerifier>();
        services.AddSingleton<IClientSecretHasher, DefaultClientSecretHasher>();
        services.AddSingleton<IConsentTokenFactory>(new DemoConsentTokenFactory());
        return services;
    }
}
