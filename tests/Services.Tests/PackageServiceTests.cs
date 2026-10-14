using Contracts;
using Microsoft.EntityFrameworkCore;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class PackageServiceTests(DatabaseFixture database)
{
    private async Task CleanPackagesAsync()
    {
        await database.SqlAsync("""
            DELETE FROM PaymentEvents;
            DELETE FROM Complaints;
            UPDATE CreditTransactions SET OrderId = NULL WHERE OrderId IS NOT NULL;
            DELETE FROM Orders;
            DELETE FROM Packages;
            """);
    }

    [Fact]
    public async Task GetActivePackagesAsync_returns_only_active_packages_ordered_by_display_order()
    {
        await CleanPackagesAsync();
        await database.SqlAsync("""
            INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder) VALUES
              (N'Second Active', 5, 200000, 1, 0, N'Feature 1', 2),
              (N'First Active', 1, 50000, 1, 1, N'Feature 2', 1),
              (N'Inactive Hidden', 10, 350000, 0, 0, N'Hidden feature', 0);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new PackageService(db);

        var packages = await service.GetActivePackagesAsync();

        Assert.Equal(2, packages.Count);
        Assert.Equal("First Active", packages[0].Name);
        Assert.Equal("Second Active", packages[1].Name);
        Assert.True(packages[0].IsFeatured);
        Assert.False(packages[1].IsFeatured);
        Assert.DoesNotContain(packages, p => p.Name == "Inactive Hidden");
    }

    [Fact]
    public async Task Inactive_packages_are_hidden_from_pricing()
    {
        await CleanPackagesAsync();
        await database.SqlAsync("""
            INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder) VALUES
              (N'On Sale Pkg', 5, 199000, 1, 0, N'Features', 1),
              (N'Hidden Pkg 1', 1, 49000, 0, 0, N'Features', 2),
              (N'Hidden Pkg 2', 10, 349000, 0, 0, N'Features', 3);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new PackageService(db);

        var packages = await service.GetActivePackagesAsync();

        var single = Assert.Single(packages);
        Assert.Equal("On Sale Pkg", single.Name);
    }

    [Fact]
    public async Task Only_one_featured_package_at_a_time_when_creating_package()
    {
        await CleanPackagesAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new PackageService(db);

        var first = await service.CreatePackageAsync(new CreatePackageRequest(
            Name: "Package 1",
            Credits: 1,
            Price: 49000,
            IsActive: true,
            IsFeatured: true,
            Features: "Bullet 1\nBullet 2",
            DisplayOrder: 1));

        Assert.True(first.IsFeatured);

        // Create second package also marked as featured -> must unfeature first
        var second = await service.CreatePackageAsync(new CreatePackageRequest(
            Name: "Package 2",
            Credits: 5,
            Price: 199000,
            IsActive: true,
            IsFeatured: true,
            Features: "Bullet A\nBullet B",
            DisplayOrder: 2));

        Assert.True(second.IsFeatured);

        // Verify in database: exactly one package has IsFeatured = true
        var allPackages = await db.Packages.AsNoTracking().ToListAsync();
        var featuredPackages = allPackages.Where(p => p.IsFeatured).ToList();
        var singleFeatured = Assert.Single(featuredPackages);
        Assert.Equal(second.PackageId, singleFeatured.PackageId);

        var pkg1InDb = allPackages.Single(p => p.PackageId == first.PackageId);
        Assert.False(pkg1InDb.IsFeatured);
    }

    [Fact]
    public async Task Only_one_featured_package_at_a_time_when_updating_package()
    {
        await CleanPackagesAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new PackageService(db);

        var p1 = await service.CreatePackageAsync(new CreatePackageRequest("Package 1", 1, 49000, true, true, null, 1));
        var p2 = await service.CreatePackageAsync(new CreatePackageRequest("Package 2", 5, 199000, true, false, null, 2));

        Assert.True((await service.GetPackageByIdAsync(p1.PackageId))!.IsFeatured);
        Assert.False((await service.GetPackageByIdAsync(p2.PackageId))!.IsFeatured);

        // Update p2 to be featured
        await service.UpdatePackageAsync(p2.PackageId, new UpdatePackageRequest(
            Name: "Package 2 Updated",
            Credits: 5,
            Price: 199000,
            IsActive: true,
            IsFeatured: true,
            Features: null,
            DisplayOrder: 2));

        var updatedP1 = await service.GetPackageByIdAsync(p1.PackageId);
        var updatedP2 = await service.GetPackageByIdAsync(p2.PackageId);

        Assert.False(updatedP1!.IsFeatured);
        Assert.True(updatedP2!.IsFeatured);

        var featuredCount = await db.Packages.CountAsync(p => p.IsFeatured);
        Assert.Equal(1, featuredCount);
    }

    [Fact]
    public async Task DeactivatePackage_hides_package_from_public_pricing()
    {
        await CleanPackagesAsync();
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new PackageService(db);

        var created = await service.CreatePackageAsync(new CreatePackageRequest("To Deactivate", 5, 199000, true, false, null, 1));

        var before = await service.GetActivePackagesAsync();
        Assert.Single(before);

        await service.DeactivatePackageAsync(created.PackageId);

        var after = await service.GetActivePackagesAsync();
        Assert.Empty(after);

        // Package still exists in admin view
        var adminView = await service.GetAdminPackagesAsync();
        Assert.Single(adminView.Packages);
        Assert.False(adminView.Packages[0].IsActive);
        Assert.Equal(0, adminView.PackagesOnSale);
    }

    [Fact]
    public async Task Validation_enforces_required_name_positive_credits_and_non_negative_price()
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new PackageService(db);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreatePackageAsync(new CreatePackageRequest("", 5, 10000)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreatePackageAsync(new CreatePackageRequest("Valid Name", 0, 10000)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreatePackageAsync(new CreatePackageRequest("Valid Name", -1, 10000)));

        await Assert.ThrowsAsync<ArgumentException>(() =>
            service.CreatePackageAsync(new CreatePackageRequest("Valid Name", 5, -100)));
    }

    [Fact]
    public async Task GetAdminPackagesAsync_calculates_summary_stats()
    {
        await CleanPackagesAsync();
        await database.SqlAsync("""
            INSERT INTO Packages (Name, Credits, Price, IsActive, IsFeatured, Features, DisplayOrder) VALUES
              (N'Pay as you go', 1, 49000, 1, 0, N'1 full-test credit', 1),
              (N'Exam Prep', 5, 199000, 1, 1, N'5 full-test credits', 2),
              (N'Retired Pkg', 10, 300000, 0, 0, N'Old', 3);
            """);

        await using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new PackageService(db);

        var summary = await service.GetAdminPackagesAsync();

        Assert.Equal(3, summary.Packages.Count);
        Assert.Equal(2, summary.PackagesOnSale);
        Assert.Equal(1, summary.FeaturedCount);
        Assert.Equal(0, summary.TotalOrdersSold);
        Assert.Equal(0m, summary.TotalRevenue);
    }
}
