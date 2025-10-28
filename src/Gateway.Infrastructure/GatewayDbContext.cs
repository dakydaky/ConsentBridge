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

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.HasPostgresExtension("uuid-ossp", schema: null, database: null);
        b.Entity<Candidate>().HasIndex(x => x.EmailHash).IsUnique();
        b.Entity<Consent>().HasOne(c => c.Candidate).WithMany().HasForeignKey(c => c.CandidateId);
        b.Entity<Application>().HasOne(a => a.Consent).WithMany().HasForeignKey(a => a.ConsentId);
        base.OnModelCreating(b);
    }
}
