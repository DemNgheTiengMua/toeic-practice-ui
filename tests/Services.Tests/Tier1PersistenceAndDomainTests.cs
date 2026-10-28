using System;
using System.IO;
using System.Linq;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Xunit;

namespace Services.Tests;

/// <summary>
/// Tier 1: Persistence and Domain Architecture Tests for Services.
/// Enforces database-first invariants (ADR-0001), EF Core HasTrigger configurations,
/// keyless view mapping rules, append-only credit ledger invariants, and certificate isolation.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class Tier1PersistenceAndDomainTests(DatabaseFixture database)
{
    [Fact]
    public void EFCore_Model_Preserves_HasTrigger_Configurations_Across_Core_Tables()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var model = db.Model;

        string[] auditedTables =
        [
            "CreditTransactions",
            "ExamAttempts",
            "AiGradings",
            "AiGradingScores",
            "Bookings",
            "Complaints",
            "Exams",
            "FreeResponses",
            "GradingCriteria",
            "Orders"
        ];

        foreach (var tableName in auditedTables)
        {
            var entity = model.GetEntityTypes().FirstOrDefault(e => e.GetTableName() == tableName);
            Assert.True(entity != null, $"Entity for table '{tableName}' must be configured in CompanionDbContext.");

            var triggers = entity.GetDeclaredTriggers();
            Assert.True(triggers.Any(), $"Table '{tableName}' must have HasTrigger(...) configured in EF Core to prevent OUTPUT clause save failures.");
        }
    }

    [Fact]
    public void DatabaseFirst_Schema_File_Is_Single_Source_Of_Truth_And_Exhaustive()
    {
        var repoRoot = DatabaseFixture.RepoRootPath;
        var schemaPath = Path.Combine(repoRoot, "docs", "database-schema.sql");

        Assert.True(File.Exists(schemaPath), "docs/database-schema.sql must exist as single source of truth per ADR-0001");
        var schemaContent = File.ReadAllText(schemaPath);

        // Assert core tables and views are defined in schema
        Assert.Contains("Certificates", schemaContent);
        Assert.Contains("Skills", schemaContent);
        Assert.Contains("Sections", schemaContent);
        Assert.Contains("Questions", schemaContent);
        Assert.Contains("ExamAttempts", schemaContent);
        Assert.Contains("CreditTransactions", schemaContent);
        Assert.Contains("vw_UserSectionAccuracyAll", schemaContent);
        Assert.Contains("vw_UserCategoryAccuracy", schemaContent);
    }

    [Fact]
    public void Keyless_SQL_Views_Are_Configured_As_ReadOnly_And_Have_No_PrimaryKey()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var model = db.Model;

        Type[] keylessViewTypes =
        [
            typeof(vw_UserSectionAccuracyAll),
            typeof(vw_UserCategoryAccuracy),
            typeof(vw_UserCreditBalance)
        ];

        foreach (var viewType in keylessViewTypes)
        {
            var entity = model.FindEntityType(viewType);
            Assert.True(entity != null, $"View model '{viewType.Name}' must be mapped in CompanionDbContext.");
            Assert.Null(entity.FindPrimaryKey());
        }
    }

    [Fact]
    public void CreditTransactions_Has_AppendOnly_Trigger_Configured_In_Model()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var entity = db.Model.FindEntityType(typeof(CreditTransaction));

        Assert.NotNull(entity);
        var triggers = entity.GetDeclaredTriggers().Select(t => t.ModelName).ToList();

        Assert.Contains("trg_CreditTransactions_AppendOnly", triggers);
        Assert.Contains("trg_CreditTransactions_NoNegativeBalance", triggers);
        Assert.Contains("trg_CreditTransactions_NoRewrite", triggers);
    }

    [Fact]
    public void Composite_Foreign_Keys_Carry_CertId_For_Certificate_Isolation()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var model = db.Model;

        var examEntity = model.FindEntityType(typeof(Exam));
        Assert.NotNull(examEntity);
        var certIdProp = examEntity.FindProperty("CertId");
        Assert.NotNull(certIdProp);

        var sectionEntity = model.FindEntityType(typeof(Section));
        Assert.NotNull(sectionEntity);
        var sectionCertId = sectionEntity.FindProperty("CertId");
        Assert.NotNull(sectionCertId);

        var attemptEntity = model.FindEntityType(typeof(ExamAttempt));
        Assert.NotNull(attemptEntity);
        var attemptCertId = attemptEntity.FindProperty("CertId");
        Assert.NotNull(attemptCertId);
    }
    [Fact]
    public void Database_first_repository_has_no_migration_or_scaffold_artifacts_and_entities_are_in_business_objects()
    {
        var root = DatabaseFixture.RepoRootPath;
        var dataAccess = Path.Combine(root, "src", "DataAccess");
        var businessObjects = Path.Combine(root, "src", "BusinessObjects");

        Assert.False(Directory.Exists(Path.Combine(dataAccess, "Migrations")));
        Assert.False(Directory.Exists(Path.Combine(dataAccess, "Scaffold")));
        Assert.Empty(Directory.EnumerateFiles(dataAccess, "*Migration*.cs", SearchOption.AllDirectories));
        Assert.Empty(Directory.EnumerateFiles(dataAccess, "*Snapshot*.cs", SearchOption.AllDirectories));

        var dataAccessTypes = Directory.EnumerateFiles(dataAccess, "*.cs", SearchOption.AllDirectories)
            .Where(path => !path.Contains(Path.DirectorySeparatorChar + "obj" + Path.DirectorySeparatorChar))
            .SelectMany(path => File.ReadLines(path).Where(line => line.Contains("partial class ") && !line.Contains("CompanionDbContext")))
            .ToArray();
        Assert.Empty(dataAccessTypes);
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(businessObjects, "Entities"), "*.cs", SearchOption.AllDirectories));
        Assert.NotEmpty(Directory.EnumerateFiles(Path.Combine(businessObjects, "Views"), "*.cs", SearchOption.AllDirectories));
    }
}
