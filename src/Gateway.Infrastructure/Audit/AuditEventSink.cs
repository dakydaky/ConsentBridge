using Gateway.Domain;
using Microsoft.Extensions.Logging;

namespace Gateway.Infrastructure;

public sealed class DefaultAuditEventSink : IAuditEventSink
{
    private readonly ILogger<DefaultAuditEventSink> _logger;

    public DefaultAuditEventSink(ILogger<DefaultAuditEventSink> logger)
    {
        _logger = logger;
    }

    public Task EmitAsync(AuditEventDescriptor evt, CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("AUDIT {Category} {Action} {EntityType}={EntityId} tenant={Tenant} jti={Jti} meta={Metadata} at={At}",
            evt.Category, evt.Action, evt.EntityType, evt.EntityId, evt.Tenant, evt.Jti ?? string.Empty, evt.Metadata ?? string.Empty, evt.CreatedAt);
        return Task.CompletedTask;
    }
}

