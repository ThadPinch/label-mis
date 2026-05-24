using LabelsMis.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Microsoft.Extensions.Configuration;

namespace LabelsMis.Infrastructure;

public class LabelsMisDbContextFactory : IDesignTimeDbContextFactory<LabelsMisDbContext>
{
    public LabelsMisDbContext CreateDbContext(string[] args)
    {
        var basePath = Path.Combine(Directory.GetCurrentDirectory(), "..", "LabelsMis.Web");
        var configuration = new ConfigurationBuilder()
            .SetBasePath(basePath)
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile("appsettings.Development.json", optional: true)
            .Build();

        var connectionString = configuration.GetConnectionString("Default")
            ?? throw new InvalidOperationException("Connection string 'Default' is not configured.");

        var optionsBuilder = new DbContextOptionsBuilder<LabelsMisDbContext>();
        optionsBuilder.UseNpgsql(connectionString);

        return new LabelsMisDbContext(optionsBuilder.Options);
    }
}
