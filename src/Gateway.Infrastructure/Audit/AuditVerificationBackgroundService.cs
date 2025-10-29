using System.Text.Json;
using Gateway.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gateway.Infrastructure;

public sealed class AuditVerificationOptions
{
    public int SweepHours { get; set; } = 24; // how often to run
    public int WindowDays { get; set; } = 1;  // how many days to verify per run
    public int OverlapMinutes { get; set; } = 5; // overlap to bridge gaps
    public string DigestOutputPath { get; set; } = "/app/audit-digests"; // where to write JSON digests
    public string? DigestArchivePath { get; set; } // optional off-cluster/export path
}

public sealed class AuditVerificationBackgroundService : BackgroundService
{
    private readonly IServiceProvider _services;
    private readonly AuditVerificationOptions _options;
    private readonly ILogger<AuditVerificationBackgroundService> _logger;
    private readonly IHostEnvironment _env;

    public AuditVerificationBackgroundService(
        IServiceProvider services,
        IOptions<AuditVerificationOptions> options,
        ILogger<AuditVerificationBackgroundService> logger,
        IHostEnvironment env)
    {
        _services = services;
        _options = options.Value;
        _logger = logger;
        _env = env;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Initial run
        await RunOnceAsync(stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var delay = TimeSpan.FromHours(Math.Max(1, _options.SweepHours));
                await Task.Delay(delay, stoppingToken);
                await RunOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit verification sweep failed.");
            }
        }
    }

    private async Task RunOnceAsync(CancellationToken cancellationToken)
    {
        using var scope = _services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<GatewayDbContext>();
        var verifier = scope.ServiceProvider.GetRequiredService<IAuditVerifier>();

        var tenants = await db.Tenants.AsNoTracking()
            .Select(t => t.Slug)
            .ToListAsync(cancellationToken);
        if (tenants.Count == 0) return;

        var end = DateTime.UtcNow;
        var start = end.AddDays(-Math.Max(1, _options.WindowDays)).AddMinutes(-Math.Max(0, _options.OverlapMinutes));

        foreach (var slug in tenants)
        {
            try
            {
                var result = await verifier.VerifyAsync(slug, start, end, cancellationToken);
                if (result.Success)
                {
                    GatewayMetrics.AuditVerificationSuccess.Add(1, new KeyValuePair<string, object?>("tenant", slug));
                }
                else
                {
                    GatewayMetrics.AuditVerificationFailed.Add(1, new KeyValuePair<string, object?>("tenant", slug));
                }
                await WriteDigestAsync(result, cancellationToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Audit verification failed for tenant {Tenant}", slug);
                GatewayMetrics.AuditVerificationFailed.Add(1, new KeyValuePair<string, object?>("tenant", slug));
            }
        }
    }

    private async Task WriteDigestAsync(AuditVerificationResult result, CancellationToken cancellationToken)
    {
        var path = _options.DigestOutputPath;
        if (!Path.IsPathRooted(path))
        {
            path = Path.Combine(_env.ContentRootPath, path);
        }
        Directory.CreateDirectory(path);

        var fileName = $"{Sanitize(result.TenantSlug)}-{result.WindowEndUtc:yyyyMMddHHmmss}.json";
        var fullPath = Path.Combine(path, fileName);

        var payload = new
        {
            tenant = result.TenantSlug,
            windowStartUtc = result.WindowStartUtc,
            windowEndUtc = result.WindowEndUtc,
            previousHash = result.PreviousHash,
            computedHash = result.ComputedHash,
            success = result.Success,
            error = result.Error,
            createdAtUtc = DateTime.UtcNow
        };

        var json = JsonSerializer.Serialize(payload, new JsonSerializerOptions(JsonSerializerDefaults.Web) { WriteIndented = true });
        // Write local digest (overwrite allowed for idempotence)
        await File.WriteAllTextAsync(fullPath, json, cancellationToken);

        // Optional archive to off-cluster/WORM-like path
        if (!string.IsNullOrWhiteSpace(_options.DigestArchivePath))
        {
            var archiveRoot = _options.DigestArchivePath!;
            if (!Path.IsPathRooted(archiveRoot))
            {
                archiveRoot = Path.Combine(_env.ContentRootPath, archiveRoot);
            }
            Directory.CreateDirectory(archiveRoot);
            var archivePath = Path.Combine(archiveRoot, fileName);
            try
            {
                // Create new file (fail if exists), set read-only attribute to emulate WORM
                await using var fs = new FileStream(archivePath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                var bytes = System.Text.Encoding.UTF8.GetBytes(json);
                await fs.WriteAsync(bytes, 0, bytes.Length, cancellationToken);
                fs.Flush(true);
                fs.Close();
                var attr = File.GetAttributes(archivePath);
                File.SetAttributes(archivePath, attr | FileAttributes.ReadOnly);
            }
            catch (IOException)
            {
                // Already archived; ignore
            }
        }
    }

    private static string Sanitize(string value)
    {
        foreach (var ch in Path.GetInvalidFileNameChars())
        {
            value = value.Replace(ch, '_');
        }
        return value;
    }
}
