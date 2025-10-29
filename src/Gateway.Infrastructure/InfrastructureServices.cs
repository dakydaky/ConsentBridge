using System.Text;
using Gateway.Domain;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Logging;

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
        services.AddScoped<IConsentTokenFactory>(sp =>
        {
            var db = sp.GetRequiredService<GatewayDbContext>();
            var dp = sp.GetRequiredService<Microsoft.AspNetCore.DataProtection.IDataProtectionProvider>();
            var opts = sp.GetRequiredService<IOptions<ConsentTokenOptions>>();
            var logger = sp.GetRequiredService<ILogger<JwtConsentTokenFactory>>();
            var audit = sp.GetService<IAuditEventSink>();
            return new JwtConsentTokenFactory(db, dp, opts, logger, audit);
        });
        services.AddScoped<IConsentKeyRotator>(sp =>
            (JwtConsentTokenFactory)sp.GetRequiredService<IConsentTokenFactory>());
        services.AddScoped<IDsrService, DsrService>();
        services.Configure<ConsentLifecycleOptions>(configuration.GetSection("ConsentLifecycle"));
        services.PostConfigure<ConsentLifecycleOptions>(options =>
        {
            if (options.RenewalLeadDays < 0)
            {
                throw new InvalidOperationException("ConsentLifecycle:RenewalLeadDays must be >= 0.");
            }
            if (options.ExpiryGraceDays < 0)
            {
                throw new InvalidOperationException("ConsentLifecycle:ExpiryGraceDays must be >= 0.");
            }
        });
        services.AddScoped<IConsentLifecycleService, ConsentLifecycleService>();
        services.AddScoped<IAuditEventSink, PersistentAuditEventSink>();
        services.AddScoped<IAuditVerifier, AuditVerifierService>();
        services.Configure<AuditVerificationOptions>(configuration.GetSection("AuditVerification"));
        services.AddHostedService<AuditVerificationBackgroundService>();
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


