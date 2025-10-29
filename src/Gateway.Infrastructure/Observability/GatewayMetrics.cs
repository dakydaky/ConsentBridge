using System.Diagnostics.Metrics;

namespace Gateway.Infrastructure;

public static class GatewayMetrics
{
    public static readonly Meter Meter = new("ConsentBridge.Gateway");

    public static readonly Counter<long> ConsentRenewalsSuccess = Meter.CreateCounter<long>("consent.renewals.success");
    public static readonly Counter<long> ConsentRenewalsDenied = Meter.CreateCounter<long>("consent.renewals.denied");

    public static readonly Counter<long> AppTokenGraceAccepted = Meter.CreateCounter<long>("applications.token_grace.accepted");
    public static readonly Counter<long> AppTokenGraceRejected = Meter.CreateCounter<long>("applications.token_grace.rejected");

    public static readonly Counter<long> AuditVerificationSuccess = Meter.CreateCounter<long>("audit.verification.success");
    public static readonly Counter<long> AuditVerificationFailed = Meter.CreateCounter<long>("audit.verification.failed");
}

