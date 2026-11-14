using System.Data.Common;
using System.Diagnostics;

namespace Services.Tests;

/// <summary>
/// One SQL Server database per test run, loaded fresh from docs/database-schema.sql — the script
/// is the schema (ADR-0001), so tests always run against the real thing, triggers and views
/// included. Locally this is LocalDB; CI sets TEST_DB_CONNECTION to a full base connection
/// string (server plus credentials) pointing at its SQL Server container. sqlcmd is invoked
/// with -I so the schema's quoted/ANSI settings hold.
/// </summary>
public sealed class DatabaseFixture : IAsyncLifetime
{
    // Default is per-worktree, not one shared name. Two tickets running in parallel in two worktrees
    // otherwise drop and recreate each other's test database mid-run, and the symptom is not a
    // failure: the losing run reports fewer tests than exist, because its fixture died while
    // building. Measured 2026-11-06: 933, 705, 933 on the same commit, from nothing but overlap.
    private static readonly string Database =
        Environment.GetEnvironmentVariable("TEST_DB_NAME") ?? DefaultDatabaseName();

    /// <summary>A catalog name derived from this checkout's path, so two worktrees cannot collide
    /// while a single worktree stays stable across runs.</summary>
    private static string DefaultDatabaseName()
    {
        var root = FindRepoRoot();
        var hash = 0;
        foreach (var c in root)
        {
            hash = unchecked((hash * 31) + c);
        }

        return $"EE_ServicesTest_{Math.Abs(hash) % 100000}";
    }

    private static readonly string BaseConnectionString =
        Environment.GetEnvironmentVariable("TEST_DB_CONNECTION")
        ?? @"Server=(localdb)\MSSQLLocalDB;Trusted_Connection=True;TrustServerCertificate=True";

    public string ConnectionString { get; } = $"{BaseConnectionString};Database={Database}";

    private static string RepoRoot { get; } = FindRepoRoot();
    public static string RepoRootPath => RepoRoot;

    /// <summary>Locally sqlcmd is on PATH; on CI it lives inside the SQL Server container image
    /// at a path the runner doesn't search. The env var wins, then the known container paths,
    /// then bare "sqlcmd" for local dev.</summary>
    private static string SqlCmdPath { get; } = ResolveSqlCmdPath();

    private static string ResolveSqlCmdPath()
    {
        var fromEnv = Environment.GetEnvironmentVariable("SQLCMD_PATH");
        if (!string.IsNullOrEmpty(fromEnv))
        {
            return fromEnv;
        }

        foreach (var candidate in new[]
        {
            "/opt/mssql-tools18/bin/sqlcmd",
            "/opt/mssql-tools/bin/sqlcmd",
        })
        {
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        return "sqlcmd";
    }

    public async Task InitializeAsync()
    {
        await SqlAsync("master",
            $"IF DB_ID('{Database}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{Database}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{Database}]; END; " +
            $"CREATE DATABASE [{Database}];");
        await SqlCmdFileAsync(Database, Path.Combine(RepoRoot, "docs", "database-schema.sql"));
        await MarkFixtureCatalogAsync(Database);
    }

    public Task DisposeAsync() => Task.CompletedTask;

    public async Task<string> CreateFreshDatabaseAsync(string databaseName)
    {
        await SqlAsync("master",
            $"IF DB_ID('{databaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END; " +
            $"CREATE DATABASE [{databaseName}];");
        await SqlCmdFileAsync(databaseName, Path.Combine(RepoRoot, "docs", "database-schema.sql"));
        await MarkFixtureCatalogAsync(databaseName);
        return $"{BaseConnectionString};Database={databaseName}";
    }

    // NO ApplyPendingSchemaDeltasAsync. A previous wave let a ticket write docs/schema-changes-<n>.sql
    // when it did not own database-schema.sql, and this fixture applied the set - so the tests had a
    // table the product did not, and every practice answer returned 500 with "Invalid object name
    // 'UserCellMasteries'" on the real app while the suite stayed green. database-schema.sql is the
    // single source of truth (ADR-0001): a table that is not in it does not exist.

    /// <summary>
    /// #201: this catalog holds disposable test data, so the schema's five write guards do not demand
    /// an author on an unattributed fixture write. The triage rule itself is untouched: a write that
    /// declares a triage-only account is still refused here, and <c>domain35</c> proves both halves.
    /// </summary>
    private static Task MarkFixtureCatalogAsync(string database) =>
        SqlAsync(database, "INSERT INTO CatalogMode (ModeId, IsFixtureCatalog) VALUES (1, 1);");

    public async Task DropDatabaseAsync(string databaseName)
    {
        Microsoft.Data.SqlClient.SqlConnection.ClearAllPools();
        await SqlAsync("master",
            $"IF DB_ID('{databaseName}') IS NOT NULL BEGIN " +
            $"ALTER DATABASE [{databaseName}] SET SINGLE_USER WITH ROLLBACK IMMEDIATE; DROP DATABASE [{databaseName}]; END;");
    }

    /// <summary>Run an ad-hoc batch against the test database (used to seed rows per test).</summary>
    public Task SqlAsync(string batch) => SqlAsync(Database, batch);

    private static async Task SqlAsync(string database, string query)
    {
        var result = await RunSqlCmdAsync(database, ("-Q", query));
        Assert.True(result.ExitCode == 0, $"sqlcmd failed ({result.ExitCode}): {result.Output}");
    }

    /// <summary>Run a .sql file against the test database (TEST_DB_NAME when set).</summary>
    public Task SqlCmdFileAsync(string scriptPath) => SqlCmdFileAsync(Database, scriptPath);

    public Task SqlCmdFileToDatabaseAsync(string database, string scriptPath) => SqlCmdFileAsync(database, scriptPath);

    private static async Task SqlCmdFileAsync(string database, string scriptPath)
    {
        var result = await RunSqlCmdAsync(database, ("-i", scriptPath));
        Assert.True(result.ExitCode == 0, $"sqlcmd failed loading {scriptPath} ({result.ExitCode}): {result.Output}");
    }

    private static async Task<(int ExitCode, string Output)> RunSqlCmdAsync(
        string database, params (string Flag, string Value)[] args)
    {
        var builder = new DbConnectionStringBuilder { ConnectionString = BaseConnectionString };
        var server = builder["Server"].ToString()!;
        var trusted = builder.TryGetValue("Trusted_Connection", out var tc) && tc.ToString() == "True";

        var argumentList = new List<string> { "-S", server, "-d", database, "-I", "-b", "-C", "-f", "65001" };
        if (!trusted)
        {
            argumentList.AddRange(["-U", builder["User ID"].ToString()!, "-P", builder["Password"].ToString()!]);
        }

        foreach (var (flag, value) in args)
        {
            argumentList.Add(flag);
            argumentList.Add(value);
        }

        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = SqlCmdPath,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            },
        };
        foreach (var argument in argumentList)
        {
            process.StartInfo.ArgumentList.Add(argument);
        }

        process.Start();
        var stdout = await process.StandardOutput.ReadToEndAsync();
        var stderr = await process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        return (process.ExitCode, stdout + stderr);
    }

    private static string FindRepoRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "docs", "database-schema.sql")))
        {
            directory = directory.Parent;
        }

        Assert.True(directory is not null, "Could not locate the repo root containing docs/database-schema.sql.");
        return directory.FullName;
    }
}

[CollectionDefinition(Name)]
public sealed class DatabaseCollection : ICollectionFixture<DatabaseFixture>
{
    public const string Name = "Database";
}
