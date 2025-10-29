using Gateway.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gateway.Infrastructure;

public class GatewayDbContext : DbContext
{
    public GatewayDbContext(DbContextOptions<GatewayDbContext> options) : base(options)
    {
    }

    public DbSet<Candidate> Candidates => Set<Candidate>();
    public DbSet<Consent> Consents => Set<Consent>();
    public DbSet<Application> Applications => Set<Application>();
    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<TenantCredential> TenantCredentials => Set<TenantCredential>();
    public DbSet<ConsentRequest> ConsentRequests => Set<ConsentRequest>();
    public DbSet<TenantKey> TenantKeys => Set<TenantKey>();
    public DbSet<ConsentTokenRecord> ConsentTokens => Set<ConsentTokenRecord>();
    public DbSet<AuditEvent> AuditEvents => Set<AuditEvent>();
    public DbSet<AuditEventHash> AuditEventHashes => Set<AuditEventHash>();
    public DbSet<AuditVerificationRun> AuditVerificationRuns => Set<AuditVerificationRun>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("uuid-ossp");
        b.Entity<Candidate>().HasIndex(x => x.EmailHash).IsUnique();
        b.Entity<Consent>().HasOne(c => c.Candidate).WithMany().HasForeignKey(c => c.CandidateId);
        b.Entity<Application>().HasOne(a => a.Consent).WithMany().HasForeignKey(a => a.ConsentId);
        b.Entity<Consent>().HasIndex(c => c.TokenId).IsUnique();
        b.Entity<Consent>().Property(c => c.TokenHash).HasMaxLength(128);
        b.Entity<Consent>().Property(c => c.TokenKeyId).HasMaxLength(64);
        b.Entity<Consent>().Property(c => c.TokenAlgorithm).HasMaxLength(32);
        b.Entity<Tenant>().HasIndex(t => t.Slug).IsUnique();
        b.Entity<TenantCredential>().HasIndex(tc => tc.ClientId).IsUnique();
        b.Entity<TenantCredential>()
            .HasOne(tc => tc.Tenant)
            .WithMany(t => t.Credentials)
            .HasForeignKey(tc => tc.TenantId);
        b.Entity<TenantKey>()
            .HasIndex(k => new { k.TenantId, k.Purpose, k.KeyId })
            .IsUnique();
        b.Entity<TenantKey>()
            .HasOne(k => k.Tenant)
            .WithMany(t => t.Keys)
            .HasForeignKey(k => k.TenantId);
        b.Entity<TenantKey>()
            .Property(k => k.KeyId)
            .HasMaxLength(64);
        b.Entity<TenantKey>()
            .Property(k => k.PublicJwk)
            .HasColumnType("jsonb");
        b.Entity<TenantKey>()
            .Property(k => k.PrivateKeyProtected)
            .HasColumnType("bytea");
        b.Entity<ConsentTokenRecord>()
            .HasIndex(ct => ct.TokenId)
            .IsUnique();
        b.Entity<ConsentTokenRecord>()
            .HasIndex(ct => ct.TokenHash);
        b.Entity<ConsentTokenRecord>()
            .HasOne(ct => ct.Consent)
            .WithMany()
            .HasForeignKey(ct => ct.ConsentId);
        b.Entity<ConsentTokenRecord>()
            .Property(ct => ct.TokenHash)
            .HasMaxLength(128);
        b.Entity<ConsentTokenRecord>()
            .Property(ct => ct.KeyId)
            .HasMaxLength(64);
        b.Entity<ConsentTokenRecord>()
            .Property(ct => ct.Algorithm)
            .HasMaxLength(32);
        b.Entity<ConsentRequest>()
            .HasIndex(cr => cr.AgentTenantId);
        b.Entity<ConsentRequest>()
            .HasIndex(cr => new { cr.CandidateEmail, cr.Status });
        b.Entity<ConsentRequest>()
            .HasOne(cr => cr.Consent)
            .WithMany()
            .HasForeignKey(cr => cr.ConsentId);

        b.Entity<AuditEvent>()
            .HasKey(a => a.Id);
        b.Entity<AuditEvent>()
            .Property(a => a.Category).HasMaxLength(64);
        b.Entity<AuditEvent>()
            .Property(a => a.Action).HasMaxLength(64);
        b.Entity<AuditEvent>()
            .Property(a => a.EntityType).HasMaxLength(64);
        b.Entity<AuditEvent>()
            .Property(a => a.EntityId).HasMaxLength(128);
        b.Entity<AuditEvent>()
            .Property(a => a.PayloadHash).HasMaxLength(128);
        b.Entity<AuditEvent>()
            .Property(a => a.Jti).HasMaxLength(64);
        b.Entity<AuditEvent>()
            .HasIndex(a => new { a.TenantId, a.Category, a.CreatedAt });

        b.Entity<AuditEventHash>()
            .HasKey(h => h.EventId);
        b.Entity<AuditEventHash>()
            .Property(h => h.PreviousHash).HasMaxLength(128);
        b.Entity<AuditEventHash>()
            .Property(h => h.CurrentHash).HasMaxLength(128);
        b.Entity<AuditEventHash>()
            .HasIndex(h => new { h.TenantChainId, h.CreatedAt });
        
        b.Entity<AuditVerificationRun>()
            .HasKey(v => v.Id);
        b.Entity<AuditVerificationRun>()
            .HasIndex(v => new { v.TenantId, v.CreatedAtUtc });
        base.OnModelCreating(b);
    }
}
