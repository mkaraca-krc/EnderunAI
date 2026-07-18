using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace EnderunAI.Api.Data;

public sealed class AppDbContextFactory
    : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("DB_CONNECTION");

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Migration işlemi için DB_CONNECTION tanımlı değil.");
        }

        var optionsBuilder =
            new DbContextOptionsBuilder<AppDbContext>();

        optionsBuilder.UseNpgsql(
            connectionString,
            npgsqlOptions =>
            {
                npgsqlOptions.MigrationsAssembly(
                    typeof(AppDbContext).Assembly.FullName);
            });

        return new AppDbContext(optionsBuilder.Options);
    }
}
