using BusinessObjects;
using Contracts;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Services.Tests;

/// <summary>
/// ADM-01 / D16: staff authority is five composable namespaces, stored as data, and an account
/// reads them from its role instead of being identified by a job title. Issue #201 builds the two
/// separation-of-duty invariants on top of this shape, so the shape has to be a real table a
/// trigger can read, not a string comparison in a controller.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class PermissionNamespacesTests(DatabaseFixture database)
{
    private static readonly string[] Codes = [.. Permissions.All];

    private static async Task RunSqlAsync(string connectionString, string commandText)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand(commandText, connection);
        await command.ExecuteNonQueryAsync();
    }

    /// <summary>Seeds the closed set. The schema declares the five in a CHECK but no rows, so a
    /// database loaded from the schema alone has nothing to grant until this runs.</summary>
    private async Task SeedPermissionsAsync()
    {
        foreach (var code in Codes)
        {
            await RunSqlAsync(database.ConnectionString, $"""
                IF NOT EXISTS (SELECT 1 FROM Permissions WHERE Code = '{code}')
                  INSERT INTO Permissions (Code) VALUES ('{code}');
                """);
        }
    }

    /// <summary>The five codes live in one place in C#; the schema's CHECK is the other place, and a
    /// one-sided rename would silently stop gating a route.</summary>
    [Fact]
    public void The_five_codes_the_code_knows_are_the_five_the_spec_names()
    {
        Assert.Equal(
            new[]
            {
                "assessment.blueprint.design", "operations.support.triage", "pool.content.author",
                "pool.content.review", "system.governance.admin",
            },
            Permissions.All.OrderBy(c => c, StringComparer.Ordinal));
    }

    [Fact]
    public async Task The_schema_accepts_the_five_namespaces_and_refuses_a_sixth()
    {
        await SeedPermissionsAsync();

        // ADM-01 names five. A sixth code would be a namespace nobody designed, and a typo in a
        // policy name would otherwise sail through as a silent "nobody holds this".
        await Assert.ThrowsAsync<SqlException>(() => RunSqlAsync(database.ConnectionString, """
            INSERT INTO Permissions (Code) VALUES ('pool.content.publish');
            """));
    }

    [Fact]
    public async Task A_role_holds_any_combination_and_the_same_pair_cannot_be_granted_twice()
    {
        await SeedPermissionsAsync();
        var role = $"perm-{Guid.NewGuid():N}"[..16];

        await RunSqlAsync(database.ConnectionString, $"""
            INSERT INTO Roles (Code, Name) VALUES ('{role}', N'Author who also reviews');
            DECLARE @r INT = (SELECT RoleId FROM Roles WHERE Code = '{role}');
            INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES
              (@r, 'pool.content.author'),
              (@r, 'pool.content.review'),
              (@r, 'operations.support.triage');
            """);

        await using (var db = DbContextFactory.Create(database.ConnectionString))
        {
            var held = await db.Roles
                .Where(r => r.Code == role)
                .SelectMany(r => r.RolePermissions)
                .Select(rp => rp.PermissionCode)
                .ToListAsync();

            Assert.Equal(
                new[] { "operations.support.triage", "pool.content.author", "pool.content.review" },
                held.OrderBy(c => c, StringComparer.Ordinal));
        }

        // Three of five is a combination, not a different role: the pair is the grant.
        await Assert.ThrowsAsync<SqlException>(() => RunSqlAsync(database.ConnectionString, $"""
            INSERT INTO RolePermissions (RoleId, PermissionCode)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = '{role}'), 'pool.content.author');
            """));
    }

    /// <summary>Criterion 2 of #200: a check reads the capability, so the account carries the set.</summary>
    [Fact]
    public async Task An_account_reads_the_namespaces_its_role_holds_live()
    {
        await SeedPermissionsAsync();
        var email = $"perm-{Guid.NewGuid():N}@test.local";
        var role = $"perm-{Guid.NewGuid():N}"[..16];
        await RunSqlAsync(database.ConnectionString, $"""
            INSERT INTO Roles (Code, Name) VALUES ('{role}', N'Designer');
            DECLARE @r INT = (SELECT RoleId FROM Roles WHERE Code = '{role}');
            INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@r, 'assessment.blueprint.design');
            INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@r, '{email}', 0x01);
            """);

        var user = (await CreateAuthService().GetUserAsync(await UserIdAsync(email)))!;

        Assert.Equal([Permissions.BlueprintDesign], user.Permissions);

        // A grant added mid-session is what the next check must see: the list is read from the
        // RolePermissions rows, never snapshotted onto the account.
        await RunSqlAsync(database.ConnectionString, $"""
            INSERT INTO RolePermissions (RoleId, PermissionCode)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = '{role}'), 'pool.content.author');
            """);

        var refreshed = (await CreateAuthService().GetUserAsync(await UserIdAsync(email)))!;

        Assert.Equal(
            [Permissions.BlueprintDesign, Permissions.ContentAuthor],
            refreshed.Permissions.OrderBy(p => p, StringComparer.Ordinal));
    }

    /// <summary>The demo administrator keeps every namespace, so every staff route stays reachable
    /// on a seeded database; a learner holds none, so every staff route refuses one.</summary>
    [Fact]
    public async Task The_demo_seed_grants_every_namespace_to_the_administrator_and_none_to_a_learner()
    {
        var dbName = $"PermissionsSeed_{Guid.NewGuid():N}";
        await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var seed = Path.Combine(DatabaseFixture.RepoRootPath, "docs", "seed-demo.sql");
            await database.SqlCmdFileToDatabaseAsync(dbName, seed);
            await database.SqlCmdFileToDatabaseAsync(dbName, seed);

            var connectionString = new SqlConnectionStringBuilder(database.ConnectionString)
            {
                InitialCatalog = dbName,
            }.ConnectionString;

            async Task<int> CountAsync(string sql)
            {
                await using var connection = new SqlConnection(connectionString);
                await connection.OpenAsync();
                await using var command = new SqlCommand(sql, connection);
                return Convert.ToInt32(await command.ExecuteScalarAsync());
            }

            Assert.Equal(5, await CountAsync("SELECT COUNT(*) FROM Permissions"));
            Assert.Equal(5, await CountAsync("""
                SELECT COUNT(*) FROM RolePermissions rp
                JOIN Roles r ON r.RoleId = rp.RoleId
                WHERE r.Code = 'admin'
                """));
            Assert.Equal(0, await CountAsync("""
                SELECT COUNT(*) FROM RolePermissions rp
                JOIN Roles r ON r.RoleId = rp.RoleId
                WHERE r.Code = 'learner'
                """));
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The old rule refused to lock "an administrator", keyed on the role name. Under D16 that has
    /// to mean an account holding <c>system.governance.admin</c> — the namespace that can grant
    /// namespaces — and nothing wider: a governor must still be able to disable a rogue content
    /// author or support triager, which is what the screen is for.
    /// </summary>
    [Fact]
    public async Task Locking_is_refused_for_a_system_governor_and_allowed_for_other_staff()
    {
        await SeedPermissionsAsync();
        var suffix = Guid.NewGuid().ToString("N")[..8];

        async Task<int> SeedAccountAsync(string capability)
        {
            var role = $"staff-{suffix}-{Guid.NewGuid().ToString("N")[..4]}";
            var email = $"{role}@test.local";
            await RunSqlAsync(database.ConnectionString, $"""
                INSERT INTO Roles (Code, Name) VALUES ('{role}', N'Staff');
                DECLARE @r INT = (SELECT RoleId FROM Roles WHERE Code = '{role}');
                INSERT INTO RolePermissions (RoleId, PermissionCode) VALUES (@r, '{capability}');
                INSERT INTO Users (RoleId, Email, PasswordHash) VALUES (@r, '{email}', 0x01);
                """);
            return await UserIdAsync(email);
        }

        var governor = await SeedAccountAsync(Permissions.SystemAdmin);
        var author = await SeedAccountAsync(Permissions.ContentAuthor);
        var service = new AdminUserService(
            DbContextFactory.Create(database.ConnectionString), CreateAuthService(), TimeProvider.System);

        var refused = await Assert.ThrowsAsync<AdminUserException>(
            () => service.SetLockAsync(adminUserId: author, targetUserId: governor, isLocked: true));
        Assert.Equal(ApiError.Codes.Forbidden, refused.Code);

        // A non-governor staff account can be locked; the screen would be useless otherwise.
        var allowed = await service.SetLockAsync(adminUserId: governor, targetUserId: author, isLocked: true);
        Assert.True(allowed.IsLocked);
    }

    private AuthService CreateAuthService() => new(
        DbContextFactory.Create(database.ConnectionString),
        new RecordingEmailSender(),
        TimeProvider.System,
        new AuthLinkOptions(),
        NullLogger<AuthService>.Instance);

    private async Task<int> UserIdAsync(string email)
    {
        await using var db = DbContextFactory.Create(database.ConnectionString);
        return await db.Users.Where(u => u.Email == email).Select(u => u.UserId).SingleAsync();
    }
}
