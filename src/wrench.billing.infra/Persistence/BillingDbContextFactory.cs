using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace wrench.billing.infra.Persistence;

/// <summary>Usada apenas pelo <c>dotnet ef</c> em tempo de design (não conecta ao banco).</summary>
[System.Diagnostics.CodeAnalysis.ExcludeFromCodeCoverage]
public class BillingDbContextFactory : IDesignTimeDbContextFactory<BillingDbContext>
{
    public BillingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<BillingDbContext>()
            .UseNpgsql("Host=localhost;Database=wrench_billing;Username=design;Password=design")
            .Options;

        return new BillingDbContext(options);
    }
}
