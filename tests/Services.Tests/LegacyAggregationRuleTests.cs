using BusinessObjects;
using BusinessObjects.Entities;
using Contracts;
using DataAccess;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #171: <c>independent</c> was removed from <c>CK_Certificates_ScoreAggregationRule</c>, but a database
/// created before that change can still hold the value. The ticket asks what a read does then, so this
/// states it in executable form: the attempt and its certificate read back unchanged, because the stored
/// rule is a string that the read path passes through rather than parsing into the enum. What changed is
/// the write: anything that recomputes a total refuses with <c>aggregation_rule_unknown</c> instead of
/// silently writing another zero.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class LegacyAggregationRuleTests(DatabaseFixture database)
{
    /// <summary>
    /// A pre-#171 database. The constraint is disabled because the point of the fixture is to reach a
    /// row the current schema rejects; the database is dropped at the end of the test.
    /// </summary>
    private async Task<string> SeedLegacyIndependentCertificateAsync(string connectionString)
    {
        await using var connection = new SqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new SqlCommand("""
            IF NOT EXISTS (SELECT 1 FROM Roles WHERE Code = 'learner')
              INSERT INTO Roles (Code, Name) VALUES ('learner', N'Learner');

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName)
            VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'), 'legacy@test.local', 0x01, N'Legacy');

            ALTER TABLE Certificates NOCHECK CONSTRAINT CK_Certificates_ScoreAggregationRule;
            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule)
            VALUES ('LEGACY_INDEP', N'Legacy Independent', 1, 'independent');
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'SPEA', N'Speaking', 'speaking', 1),
              (@cert, 'WRIT', N'Writing', 'writing', 2);

            INSERT INTO Exams (CertId, Name, Status, DurationMinutes, Purpose)
            VALUES (@cert, N'Legacy exam', 'published', 60, 'mock');
            DECLARE @exam INT = SCOPE_IDENTITY();
            """, connection);
        await command.ExecuteNonQueryAsync();
        return connectionString;
    }

    [Fact]
    public async Task A_legacy_attempt_on_a_removed_rule_still_reads_but_can_no_longer_be_rescored()
    {
        var databaseName = $"LegacyRule_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await SeedLegacyIndependentCertificateAsync(connectionString);

            await using var db = DbContextFactory.Create(connectionString);
            var cert = await db.Certificates.SingleAsync(c => c.Code == "LEGACY_INDEP");

            // The read succeeds. The value is a stored string, not a parsed enum, so no read path has
            // to know the rule was retired - and none of them throws on it.
            Assert.Equal("independent", cert.ScoreAggregationRule);

            // The display factor never depended on the removed rule: it asks only whether the
            // certificate keeps tenths, and `independent` did not. So a legacy score still renders
            // at the same unit it always did rather than changing on read.
            Assert.Equal(1, ScoreDisplay.FactorFor(cert.ScoreAggregationRule));

            // The write does refuse. Every attempt on such a certificate scored zero before #171;
            // now recomputing a total names the certificate instead of producing another zero.
            var scores = new List<AttemptSkillScore>
            {
                new() { ScaledScore = 160 },
                new() { ScaledScore = 170 },
            };
            var refused = Assert.Throws<ScoreConversionException>(
                () => AttemptEngine.AggregateTotalScore(cert.ScoreAggregationRule, scores));
            Assert.Equal(ScoreConversionException.AggregationRuleUnknown, refused.Code);
            Assert.Contains("independent", refused.Message, StringComparison.Ordinal);

            // What the learner sees for the row that already exists is its stored number, unchanged.
            // Reading is not the place to invent a correction; it is the recompute that must stop.
            var observed = await db.Certificates
                .Where(c => c.CertId == cert.CertId)
                .Select(c => c.ScoreAggregationRule)
                .SingleAsync();
            Assert.Equal("independent", observed);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task The_three_surviving_rules_are_storable_and_the_removed_one_is_not()
    {
        var databaseName = $"RuleSet_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            foreach (var rule in new[] { "sum", "avg_half_step", "avg_whole" })
            {
                db.Certificates.Add(new Certificate
                {
                    Code = $"OK_{rule}".ToUpperInvariant(),
                    Name = rule,
                    ScoreAggregationRule = rule,
                    IsActive = true,
                });
            }
            await db.SaveChangesAsync();

            db.Certificates.Add(new Certificate
            {
                Code = "REMOVED",
                Name = "removed",
                ScoreAggregationRule = "independent",
                IsActive = true,
            });
            var refused = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
            Assert.IsType<SqlException>(refused.InnerException);
            Assert.Contains("CK_Certificates_ScoreAggregationRule", refused.InnerException!.Message, StringComparison.Ordinal);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }
}
