using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gateway.Infrastructure;

public class DesignTimeFactory : IDesignTimeDbContextFactory<GatewayDbContext>
{
    public GatewayDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<GatewayDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=gateway;Username=gateway;Password=devpass")
            .Options;
        return new GatewayDbContext(opts);
    }
}
