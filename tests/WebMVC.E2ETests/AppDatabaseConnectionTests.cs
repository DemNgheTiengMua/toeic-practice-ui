using Microsoft.Data.SqlClient;

namespace WebMVC.E2ETests;

/// <summary>
/// Runs without a server: pins which database the E2E helpers arrange rows in (F8), so a crawl
/// run can never mutate the everyday ExtraEfficiency database by default.
/// </summary>
[NonParallelizable]
[TestFixture]
public class AppDatabaseConnectionTests
{
    private string? _connection;
    private string? _name;

    [SetUp]
    public void SaveEnvironment()
    {
        _connection = Environment.GetEnvironmentVariable("TEST_DB_CONNECTION");
        _name = Environment.GetEnvironmentVariable("TEST_DB_NAME");
    }

    [TearDown]
    public void RestoreEnvironment()
    {
        Environment.SetEnvironmentVariable("TEST_DB_CONNECTION", _connection);
        Environment.SetEnvironmentVariable("TEST_DB_NAME", _name);
    }

    [Test]
    public void Defaults_to_the_crawl_database_on_LocalDB()
    {
        Environment.SetEnvironmentVariable("TEST_DB_CONNECTION", null);
        Environment.SetEnvironmentVariable("TEST_DB_NAME", null);

        var builder = new SqlConnectionStringBuilder(PlaywrightTestBase.AppDatabaseConnectionString());

        Assert.That(builder.InitialCatalog, Is.EqualTo("ExtraEfficiencyCrawl"));
        Assert.That(builder.DataSource, Is.EqualTo(@"(localdb)\MSSQLLocalDB"));
    }

    [Test]
    public void TEST_DB_NAME_overrides_the_catalog_of_TEST_DB_CONNECTION()
    {
        Environment.SetEnvironmentVariable("TEST_DB_CONNECTION", "Server=ci-sql;Database=Other;User ID=sa;Password=x;TrustServerCertificate=True");
        Environment.SetEnvironmentVariable("TEST_DB_NAME", "EECluster3");

        var builder = new SqlConnectionStringBuilder(PlaywrightTestBase.AppDatabaseConnectionString());

        Assert.That(builder.InitialCatalog, Is.EqualTo("EECluster3"));
        Assert.That(builder.DataSource, Is.EqualTo("ci-sql"));
    }

    [Test]
    public void A_catalog_in_TEST_DB_CONNECTION_is_kept_when_TEST_DB_NAME_is_unset()
    {
        Environment.SetEnvironmentVariable("TEST_DB_CONNECTION", @"Server=(localdb)\MSSQLLocalDB;Database=Explicit;Trusted_Connection=True");
        Environment.SetEnvironmentVariable("TEST_DB_NAME", null);

        Assert.That(new SqlConnectionStringBuilder(PlaywrightTestBase.AppDatabaseConnectionString()).InitialCatalog, Is.EqualTo("Explicit"));
    }

    [Test]
    public void A_server_only_TEST_DB_CONNECTION_gets_the_crawl_catalog()
    {
        Environment.SetEnvironmentVariable("TEST_DB_CONNECTION", "Server=ci-sql;User ID=sa;Password=x");
        Environment.SetEnvironmentVariable("TEST_DB_NAME", "  ");

        Assert.That(new SqlConnectionStringBuilder(PlaywrightTestBase.AppDatabaseConnectionString()).InitialCatalog, Is.EqualTo("ExtraEfficiencyCrawl"));
    }
}
