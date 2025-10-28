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

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("uuid-ossp");
        b.Entity<Candidate>().HasIndex(x => x.EmailHash).IsUnique();
        b.Entity<Consent>().HasOne(c => c.Candidate).WithMany().HasForeignKey(c => c.CandidateId);
        b.Entity<Application>().HasOne(a => a.Consent).WithMany().HasForeignKey(a => a.ConsentId);
        b.Entity<Consent>().HasIndex(c => c.TokenId).IsUnique();
        b.Entity<Tenant>().HasIndex(t => t.Slug).IsUnique();
        b.Entity<TenantCredential>().HasIndex(tc => tc.ClientId).IsUnique();
        b.Entity<TenantCredential>()
            .HasOne(tc => tc.Tenant)
            .WithMany(t => t.Credentials)
            .HasForeignKey(tc => tc.TenantId);
        b.Entity<ConsentRequest>()
            .HasIndex(cr => cr.AgentTenantId);
        b.Entity<ConsentRequest>()
            .HasIndex(cr => new { cr.CandidateEmail, cr.Status });
        b.Entity<ConsentRequest>()
            .HasOne(cr => cr.Consent)
            .WithMany()
            .HasForeignKey(cr => cr.ConsentId);
        base.OnModelCreating(b);
    }
}
