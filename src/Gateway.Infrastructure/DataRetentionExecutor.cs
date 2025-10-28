using Gateway.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Gateway.Infrastructure;

public sealed record RetentionCleanupResult(int ReceiptsCleared, int ConsentRequestsDeleted);

public sealed class DataRetentionExecutor
{
    private readonly GatewayDbContext _db;
    private readonly RetentionOptions _options;
    private readonly ILogger<DataRetentionExecutor> _logger;

    public DataRetentionExecutor(
        GatewayDbContext db,
        IOptions<RetentionOptions> options,
        ILogger<DataRetentionExecutor> logger)
    {
        _db = db;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<RetentionCleanupResult> RunAsync(CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var receiptCutoff = now.AddDays(-_options.ApplicationReceiptDays);
        var consentRequestCutoff = now.AddDays(-_options.ConsentRequestDays);

        int receiptsCleared = 0;
        int consentRequestsDeleted = 0;

        if (SupportsBulkOperations())
        {
            receiptsCleared = await _db.Applications
                .Where(a => a.SubmittedAt <= receiptCutoff && a.Receipt != null)
                .ExecuteUpdateAsync(setters => setters.SetProperty(a => a.Receipt, a => null), cancellationToken);

            consentRequestsDeleted = await _db.ConsentRequests
                .Where(cr => cr.CreatedAt <= consentRequestCutoff)
                .ExecuteDeleteAsync(cancellationToken);
        }
        else
        {
            var expiredApplications = await _db.Applications
                .Where(a => a.SubmittedAt <= receiptCutoff)
                .ToListAsync(cancellationToken);
            foreach (var application in expiredApplications)
            {
                if (application.Receipt is not null)
                {
                    application.Receipt = null;
                    receiptsCleared++;
                }
            }

            var expiredRequests = await _db.ConsentRequests
                .Where(cr => cr.CreatedAt <= consentRequestCutoff)
                .ToListAsync(cancellationToken);
            consentRequestsDeleted = expiredRequests.Count;
            _db.ConsentRequests.RemoveRange(expiredRequests);

            if (receiptsCleared > 0 || consentRequestsDeleted > 0)
            {
                await _db.SaveChangesAsync(cancellationToken);
            }
        }

        if (receiptsCleared > 0 || consentRequestsDeleted > 0)
        {
            _logger.LogInformation("Retention cleanup removed receipts for {ReceiptsCleared} applications and deleted {ConsentRequestsDeleted} consent requests.", receiptsCleared, consentRequestsDeleted);
        }
        else
        {
            _logger.LogDebug("Retention cleanup ran with no changes.");
        }

        return new RetentionCleanupResult(receiptsCleared, consentRequestsDeleted);
    }

    private bool SupportsBulkOperations()
    {
        var provider = _db.Database.ProviderName;
        return provider is not null &&
            !provider.Contains("InMemory", StringComparison.OrdinalIgnoreCase);
    }
}
