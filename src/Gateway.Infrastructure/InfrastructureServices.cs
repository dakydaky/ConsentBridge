using System;
using System.Text;
using Gateway.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gateway.Infrastructure;

public static class InfrastructureServices
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ITenantKeyStore>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var env = sp.GetRequiredService<IHostEnvironment>();
            return new ConfigurationTenantKeyStore(config, env);
        });
        services.AddSingleton<IJwsVerifier, JwksJwsVerifier>();
        services.AddSingleton<IClientSecretHasher, DefaultClientSecretHasher>();
        services.AddSingleton<IConsentTokenFactory>(new DemoConsentTokenFactory());

        services.AddSingleton<IAccessTokenFactory, JwtAccessTokenFactory>();
        services.PostConfigure<JwtAccessTokenOptions>(options =>
        {
            if (string.IsNullOrWhiteSpace(options.SigningKey))
            {
                throw new InvalidOperationException("Auth:Jwt:SigningKey must be configured.");
            }

            if (Encoding.UTF8.GetBytes(options.SigningKey).Length < 32)
            {
                throw new InvalidOperationException("Auth:Jwt:SigningKey must be at least 256 bits.");
            }
        });

        return services;
    }
}


