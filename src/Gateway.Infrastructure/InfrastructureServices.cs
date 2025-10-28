using System;
using System.Text;
using Gateway.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Gateway.Infrastructure;

public static class InfrastructureServices
{
    public static IServiceCollection AddGatewayInfrastructure(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<ConfigurationTenantKeyStore>(sp =>
        {
            var config = sp.GetRequiredService<IConfiguration>();
            var env = sp.GetRequiredService<IHostEnvironment>();
            return new ConfigurationTenantKeyStore(config, env);
        });
        services.AddScoped<ITenantKeyStore>(sp =>
        {
            var configStore = sp.GetRequiredService<ConfigurationTenantKeyStore>();
            var factory = sp.GetRequiredService<IDbContextFactory<GatewayDbContext>>();
            return new CompositeTenantKeyStore(configStore, factory);
        });
        services.AddScoped<IJwsVerifier, JwksJwsVerifier>();
        services.AddSingleton<IClientSecretHasher, DefaultClientSecretHasher>();
        services.AddScoped<IConsentTokenFactory, JwtConsentTokenFactory>();
        services.AddScoped<IDsrService, DsrService>();
        services.Configure<RetentionOptions>(configuration.GetSection("Retention"));
        services.Configure<ConsentTokenOptions>(configuration.GetSection("ConsentTokens"));
        services.AddScoped<DataRetentionExecutor>();
        services.AddHostedService<RetentionCleanupBackgroundService>();

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

        services.AddDbContextFactory<GatewayDbContext>();

        return services;
    }
}


