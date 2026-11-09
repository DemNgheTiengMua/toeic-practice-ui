using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #244 / NFI-10: a narrative the live generator returns that contradicts the stored payload must not
/// reach a learner.
/// <para>
/// <b>The input these tests use is the real response DTO, not a hand-made string.</b> The provider
/// answers with the JSON shape <see cref="ClaudeAiInsightGenerator"/> parses into
/// <see cref="AiInsightResult"/>; feeding the guard a separate, tidier object would prove the guard
/// works on data no model produces. The recording client here returns the same envelope the live
/// client does, and the real <see cref="ClaudeAiInsightGenerator"/> parses it.
/// </para>
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class AiInsightNarrativeGuardTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    /// <summary>
    /// Sits a placement-equivalent measurement through the only writer, so the payload has one measured
    /// skill and two the certificate examines and nothing measured.
    /// </summary>
    private async Task<(int certId, string certCode, int userId)> SeedAsync(string connStr)
    {
        var certCode = Unique("NG");
        await using var db = DbContextFactory.Create(connStr);

        await db.Database.ExecuteSqlRawAsync($"""
            INSERT INTO Roles (Code, Name) VALUES ('learner', 'Learner');
            INSERT INTO Certificates (Code, Name, IsActive, ScoreAggregationRule)
              VALUES ('{certCode}', N'Cert {certCode}', 1, 'sum');
            DECLARE @cert INT = SCOPE_IDENTITY();

            INSERT INTO Skills (CertId, Code, Name, Modality, DisplayOrder) VALUES
              (@cert, 'LIST', N'Listening', 'listening', 1),
              (@cert, 'READ', N'Reading',   'reading',   2),
              (@cert, 'WRIT', N'Writing',   'writing',   3);
            DECLARE @list INT = (SELECT SkillId FROM Skills WHERE CertId = @cert AND Code = 'LIST');

            INSERT INTO Sections (SkillId, CertId, Modality, Code, Name, ExpectedQuestionCount, DisplayOrder) VALUES
              (@list, @cert, 'listening', 'L1', N'Photographs', 1, 1);
            DECLARE @sec INT = (SELECT SectionId FROM Sections WHERE CertId = @cert AND Code = 'L1');

            INSERT INTO Users (RoleId, Email, PasswordHash, FullName, ActiveCertId)
              VALUES ((SELECT RoleId FROM Roles WHERE Code = 'learner'),
                      '{Unique("u")}@test.local', 0x01, N'Guard Learner', @cert);
            DECLARE @user INT = SCOPE_IDENTITY();

            -- Two correct answers out of five, so the prompt's figures are small and a model quoting
            -- "71%" is unmistakably an invention rather than a rounding.
            INSERT INTO PracticeSessions (UserId, CertId, SectionId, Modality, QuestionCount,
                                          Status, CorrectCount, QuestionSource, StartedAt, FinishedAt)
              VALUES (@user, @cert, @sec, 'listening', 5, 'finished', 2, 'all',
                      SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET());
            """);

        var userId = await db.Users.Where(u => u.Email.StartsWith("u") && u.ActiveCertId != null)
            .OrderByDescending(u => u.UserId).Select(u => u.UserId).FirstAsync();
        var certId = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();

        // One measured skill, written by the fact's owner. Writing stays unmeasured on purpose.
        var listSkillId = await db.Skills.Where(s => s.CertId == certId && s.Code == "LIST")
            .Select(s => s.SkillId).SingleAsync();
        var writer = new SkillStateWriter(db);
        await writer.RecordAsync(userId, certId, listSkillId,
            CefrLevel.B2, 40, 4.35m, LearnerSkillStateSource.Placement);

        return (certId, certCode, userId);
    }

    /// <summary>Returns the JSON envelope a provider answers with - the shape the parser reads.</summary>
    private static string ProviderResponse(string narrative) =>
        $$"""
        {"narrative":{{System.Text.Json.JsonSerializer.Serialize(narrative)}},
         "trend":"Steady",
         "recommendations":[{"text":"Drill Listening at 40/100 in band.","severity":"High"}]}
        """;

    private sealed class RecordingClient(string response) : IClaudeClient
    {
        public string? Prompt { get; private set; }

        public Task<string> CreateMessageAsync(string userPrompt, string? systemPrompt = null,
            int maxTokens = 1500, string? model = null, CancellationToken cancellationToken = default)
        {
            Prompt = userPrompt;
            return Task.FromResult(response);
        }
    }

    [Fact]
    public async Task A_contradictory_model_response_is_rejected_and_stores_no_narrative()
    {
        var dbName = $"EEGuard_{Guid.NewGuid():N}"[..26];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var (certId, _, userId) = await SeedAsync(connStr);

            // "Your writing is at A1" - writing is examined and never measured, and A1 exists nowhere
            // in the payload. This is the invented-bottom-band sentence, and it is what a live model
            // writes when the prompt says "four skills" and the state has three.
            var response = ProviderResponse("Your writing is at A1, so begin with paragraph drills.");

            await using var db = DbContextFactory.Create(connStr);
            var service = new AiInsightService(
                db,
                new ClaudeAiInsightGenerator(new RecordingClient(response)),
                NullLogger<AiInsightService>.Instance,
                new LearnerService(db),
                new SkillStateWriter(db));

            var pending = await service.GetOrCreateDashboardInsightAsync(userId, certId);
            await service.ProcessPendingInsightsAsync();

            await using var check = DbContextFactory.Create(connStr);
            var stored = await check.AiInsights.SingleAsync(i => i.InsightId == pending.InsightId);

            Assert.Equal(AiInsightStatus.Failed.ToCode(), stored.Status);
            Assert.Null(stored.Narrative);
            Assert.NotNull(stored.FailureReason);
            Assert.Contains("rejected", stored.FailureReason!, StringComparison.OrdinalIgnoreCase);
            Assert.Contains("A1", stored.FailureReason!);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// The other half: a response that only quotes the payload is stored. Without this, a guard that
    /// refuses every narrative would pass the test above and break the product.
    /// </summary>
    [Fact]
    public async Task A_faithful_model_response_is_stored()
    {
        var dbName = $"EEGuardOk_{Guid.NewGuid():N}"[..26];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var (certId, _, userId) = await SeedAsync(connStr);

            var response = ProviderResponse(
                "Listening is at B2, 40/100 inside the band. Writing is not measured yet, so a placement will fill it in.");

            await using var db = DbContextFactory.Create(connStr);
            var service = new AiInsightService(
                db,
                new ClaudeAiInsightGenerator(new RecordingClient(response)),
                NullLogger<AiInsightService>.Instance,
                new LearnerService(db),
                new SkillStateWriter(db));

            var pending = await service.GetOrCreateDashboardInsightAsync(userId, certId);
            await service.ProcessPendingInsightsAsync();

            await using var check = DbContextFactory.Create(connStr);
            var stored = await check.AiInsights.SingleAsync(i => i.InsightId == pending.InsightId);

            Assert.True(stored.Status == AiInsightStatus.Done.ToCode(), stored.FailureReason);
            Assert.NotNull(stored.Narrative);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }

    /// <summary>
    /// Criterion 1, asserted on the payload the service actually builds rather than on a literal: the
    /// prompt carries the four coordinates, the overall, the limiting skill, the target and the verdict.
    /// Sending them only in the request object would let the prompt drift away from the guard.
    /// </summary>
    [Fact]
    public async Task The_prompt_carries_every_stored_fact_the_narrative_may_use()
    {
        var dbName = $"EEGuardP_{Guid.NewGuid():N}"[..26];
        var connStr = await database.CreateFreshDatabaseAsync(dbName);
        try
        {
            var (certId, _, userId) = await SeedAsync(connStr);
            var client = new RecordingClient(ProviderResponse("Listening is at B2."));

            await using var db = DbContextFactory.Create(connStr);
            var service = new AiInsightService(
                db,
                new ClaudeAiInsightGenerator(client),
                NullLogger<AiInsightService>.Instance,
                new LearnerService(db),
                new SkillStateWriter(db));

            await service.GetOrCreateDashboardInsightAsync(userId, certId);
            await service.ProcessPendingInsightsAsync();

            Assert.NotNull(client.Prompt);
            Assert.Contains("[STORED FACTS", client.Prompt!);
            Assert.Contains("Listening (LIST, listening): level B2, 40/100 in band, coordinate 4.35", client.Prompt!);
            Assert.Contains("Reading (READ, reading): not measured, no level", client.Prompt!);
            Assert.Contains("Writing (WRIT, writing): not measured, no level", client.Prompt!);
            Assert.Contains("Overall (weakest measured skill): B2", client.Prompt!);
            Assert.Contains("Limiting skill: LIST", client.Prompt!);
            Assert.Contains("Feasibility: cannot judge", client.Prompt!);
        }
        finally
        {
            await database.DropDatabaseAsync(dbName);
        }
    }
}
