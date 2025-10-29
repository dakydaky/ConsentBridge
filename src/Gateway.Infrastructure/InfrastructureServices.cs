using System.Text;
using Gateway.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

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
            var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
            return new CompositeTenantKeyStore(configStore, scopeFactory);
        });
        services.AddScoped<IJwsVerifier, JwksJwsVerifier>();
        services.AddSingleton<IClientSecretHasher, DefaultClientSecretHasher>();
        services.AddScoped<IConsentTokenFactory, JwtConsentTokenFactory>();
        services.AddScoped<IConsentKeyRotator>(sp =>
            (JwtConsentTokenFactory)sp.GetRequiredService<IConsentTokenFactory>());
        services.AddScoped<IDsrService, DsrService>();
        services.Configure<ConsentLifecycleOptions>(configuration.GetSection("ConsentLifecycle"));
        services.AddScoped<IConsentLifecycleService, ConsentLifecycleService>();
        services.AddSingleton<IAuditEventSink, DefaultAuditEventSink>();
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

        return services;
    }
}


