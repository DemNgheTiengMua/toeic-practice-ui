using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

internal static class DbContextFactory
{
    public static CompanionDbContext Create(string connectionString)
    {
        var options = new DbContextOptionsBuilder<CompanionDbContext>()
            .UseSqlServer(connectionString)
            .Options;
        return new CompanionDbContext(options);
    }
}
