using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Gateway.Domain;
using Microsoft.Extensions.Options;

namespace Gateway.Infrastructure;

public static class InfrastructureServices
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IJwsVerifier, AcceptAllVerifier>();
        services.AddSingleton<IClientSecretHasher, DefaultClientSecretHasher>();
        services.AddSingleton<IConsentTokenFactory>(new DemoConsentTokenFactory());

        services.AddSingleton<IOptions<JwtAccessTokenOptions>>(_ =>
        {
            var options = new JwtAccessTokenOptions();
            configuration.GetSection("Auth:Jwt").Bind(options);
            if (string.IsNullOrWhiteSpace(options.SigningKey))
            {
                throw new InvalidOperationException("Auth:Jwt:SigningKey must be configured.");
            }
            return Options.Create(options);
        });

        services.AddSingleton<IAccessTokenFactory, JwtAccessTokenFactory>();
        return services;
    }
}


