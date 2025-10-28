namespace Gateway.Infrastructure;

public sealed class RetentionOptions
{
    public int ApplicationReceiptDays { get; set; } = 365;
    public int ConsentRequestDays { get; set; } = 90;
    public int SweepHours { get; set; } = 24;
}
