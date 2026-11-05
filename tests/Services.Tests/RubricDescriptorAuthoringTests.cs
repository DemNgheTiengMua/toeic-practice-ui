using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DataAccess;
using BusinessObjects.Entities;
using Microsoft.EntityFrameworkCore;
using Services;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #192: the authored half. A criterion saved through the wizard carries its seven-band grid, the
/// validator refuses a half-written one before anything is written, and a complete canonical rubric
/// can actually be locked - which is the door the schema trigger guards.
/// </summary>
[Collection(DatabaseCollection.Name)]
public sealed class RubricDescriptorAuthoringTests(DatabaseFixture database)
{
    private static string Unique(string prefix) => $"{prefix}{Guid.NewGuid():N}"[..20];

    private static IReadOnlyList<Contracts.SaveGradingCriterionBandDto> Grid() =>
        Enumerable.Range(0, 7).Select(b => new Contracts.SaveGradingCriterionBandDto(
            (byte)b,
            $"Band {b}: speech rate at least {60 + b * 20} wpm, under {10 - b} pauses over 1500 ms per minute.",
            $"Error-free T-units at least {10 + b * 12}%.")).ToList();

    /// <summary>A certificate with one writing skill called <paramref name="skillCode"/>, 0-50, one section.</summary>
    private static Contracts.SaveCertificateWizardRequest Request(
        string certCode, string skillCode,
        IReadOnlyList<Contracts.SaveGradingCriterionWizardDto> criteria) =>
        new(
            CertId: null, Code: certCode, Name: certCode, Description: null,
            ScoreAggregationRule: "sum", PresetCode: "custom", IsActive: true,
            Skills:
            [
                new Contracts.SaveSkillWizardDto(
                    SkillId: null, Code: skillCode, Name: skillCode,
                    Modality: Contracts.ModalityDto.Writing, DisplayOrder: 1, DurationMinutes: 60,
                    Sections:
                    [
                        new Contracts.SaveSectionWizardDto(
                            SectionId: null, Code: skillCode + "1", Name: skillCode + " 1", DisplayOrder: 1,
                            ScoreWeight: 1m, NavigationMode: Contracts.NavigationModeDto.FullSandbox,
                            TimerPolicyJson: null, LayoutType: Contracts.LayoutTypeDto.StandardVertical,
                            OptionCount: 4, ClusterSize: 1, UnitCount: 1, HideOptionText: false,
                            RequirePassage: false, RequireAudio: false, RequireImage: false,
                            RequiredKeywordCount: 0, ExpectedQuestionCount: 10,
                            AllowedComponentTypesJson: null, ToolsConfigJson: null)
                    ])
            ],
            MinScaledScore: 0, MaxScaledScore: 50, ScoreStep: 1,
            Bands:
            [
                new Contracts.SaveScoreBandWizardDto(null, "FAIL", "Below passing", 0, 24, 1),
                new Contracts.SaveScoreBandWizardDto(null, "PASS", "Passing", 25, 50, 2)
            ],
            Criteria: criteria);

    private static Contracts.SaveGradingCriterionWizardDto Criterion(
        string skillCode, string code, int order,
        IReadOnlyList<Contracts.SaveGradingCriterionBandDto>? bands) =>
        new(null, skillCode, code, code, 6m, 1m, order, "Prose for " + code, bands);

    private static IReadOnlyList<Contracts.SaveGradingCriterionAnchorDto> Anchors() =>
        CanonicalRubric.AnchorBands
            .Select(b => new Contracts.SaveGradingCriterionAnchorDto(
                b, $"A whole band {b} answer, written by hand so the model has a benchmark and not an impression."))
            .ToList();

    private static readonly string[] CanonicalWriting = ["W_TR", "W_CC", "W_LR", "W_GR"];

    [Fact]
    public void A_grid_missing_a_band_is_refused_before_anything_is_written_and_the_reason_names_the_band()
    {
        var shortGrid = Grid().Where(b => b.Band != 6).ToList();

        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(
                Request(Unique("AW01"), "WRIT", [Criterion("WRIT", "W_TR", 1, shortGrid)])));

        Assert.Contains("W_TR", error.Message, StringComparison.Ordinal);
        Assert.Contains("0-6", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_band_outside_zero_to_six_is_refused()
    {
        var bad = Grid().Append(new Contracts.SaveGradingCriterionBandDto(7, "Band 7: beyond C2.", "None.")).ToList();

        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(
                Request(Unique("AW02"), "WRIT", [Criterion("WRIT", "W_TR", 1, bad)])));

        Assert.Contains("outside 0-6", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_complete_canonical_rubric_round_trips_its_grid_and_can_be_locked()
    {
        // The full authored path: four canonical criteria with seven bands each, saved by the wizard,
        // then locked. The lock is the point - it proves the door the schema trigger guards is
        // reachable, and that seven authored bands satisfy it.
        var certCode = Unique("AW03");
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var saved = await new CertificateService(db).SaveCertificateWizardAsync(
            Request(certCode, "WRIT",
                CanonicalWriting.Select((c, i) => Criterion("WRIT", c, i + 1, Grid())).ToList()));

        var skillId = await db.Skills
            .Where(s => s.Cert.Code == certCode)
            .Select(s => s.SkillId)
            .SingleAsync();

        var criteria = await db.GradingCriteria
            .Where(c => c.SkillId == skillId)
            .OrderBy(c => c.DisplayOrder)
            .ToListAsync();
        Assert.Equal(CanonicalWriting, criteria.Select(c => c.Code));

        foreach (var criterion in criteria)
        {
            var bands = await db.GradingCriterionDescriptors
                .Where(d => d.CriterionId == criterion.CriterionId)
                .OrderBy(d => d.Band)
                .ToListAsync();
            Assert.Equal(Enumerable.Range(0, 7).Select(b => (byte)b), bands.Select(d => d.Band));
            Assert.All(bands, d => Assert.Contains("wpm", d.Descriptor, StringComparison.Ordinal));
            Assert.All(bands, d => Assert.False(string.IsNullOrWhiteSpace(d.BinaryGates)));
        }

        // The lock door accepts: exactly four canonical codes, seven bands each.
        db.GradingRubricLocks.Add(new GradingRubricLock { SkillId = skillId, CertId = saved.CertId });
        await db.SaveChangesAsync();

        await using var check = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(1, await check.GradingRubricLocks.CountAsync(l => l.Skill.Cert.Code == certCode));
    }

    [Fact]
    public async Task A_grid_missing_a_band_cannot_be_locked_even_when_it_reaches_the_database()
    {
        // The validator is one door; the schema is the second. A grid written around the validator
        // (import, migration, direct SQL) still cannot be locked, which is the invariant that matters.
        var certCode = Unique("AW04");
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var saved = await new CertificateService(db).SaveCertificateWizardAsync(
            Request(certCode, "WRIT",
                CanonicalWriting.Select((c, i) => Criterion("WRIT", c, i + 1, Grid())).ToList()));

        var skillId = await db.Skills
            .Where(s => s.Cert.Code == certCode)
            .Select(s => s.SkillId)
            .SingleAsync();
        var target = await db.GradingCriteria
            .Where(c => c.SkillId == skillId && c.Code == "W_GR")
            .Select(c => c.CriterionId)
            .SingleAsync();

        // Delete one band behind the validator's back.
        await db.Database.ExecuteSqlRawAsync(
            "DELETE FROM GradingCriterionDescriptors WHERE CriterionId = {0} AND Band = 6", target);

        db.GradingRubricLocks.Add(new GradingRubricLock { SkillId = skillId, CertId = saved.CertId });
        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("W_GR", Flatten(error), StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_save_that_omits_the_grid_leaves_the_stored_grid_alone()
    {
        // A form that posts criteria without descriptors must not silently un-grade a rubric: seven
        // rows would vanish and the next grading would infer every band.
        var certCode = Unique("AW05");
        await using var db = DbContextFactory.Create(database.ConnectionString);

        await new CertificateService(db).SaveCertificateWizardAsync(
            Request(certCode, "WRIT",
                [Criterion("WRIT", "W_TR", 1, Grid())]));

        var certId = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();
        var criterionId = await db.GradingCriteria
            .Where(c => c.Cert.Code == certCode)
            .Select(c => c.CriterionId)
            .SingleAsync();

        // Re-save the same criterion with no Bands at all.
        await new CertificateService(db).SaveCertificateWizardAsync(
            Request(certCode, "WRIT", [Criterion("WRIT", "W_TR", 1, null)]) with { CertId = certId });

        await using var check = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(7, await check.GradingCriterionDescriptors.CountAsync(d => d.CriterionId == criterionId));
    }

    /// <summary>EF wraps a THROW in a chain of exceptions; the trigger's message is at the bottom.</summary>
    private static string Flatten(Exception e)
    {
        var parts = new List<string>();
        for (var x = e; x is not null; x = x.InnerException) parts.Add(x.Message);
        return string.Join(" | ", parts);
    }

    // ---- #193: anchors ----

    [Fact]
    public async Task An_anchor_set_round_trips_through_the_wizard_and_travels_into_the_grading_detail()
    {
        // The accept direction: three anchors authored through the wizard are stored, and the criterion
        // reads back as anchored. Without this, "authorable before the lock" would mean "possible via
        // direct SQL", which is not authorable at all.
        var certCode = Unique("AW06");
        await using var db = DbContextFactory.Create(database.ConnectionString);

        await new CertificateService(db).SaveCertificateWizardAsync(
            Request(certCode, "WRIT",
            [
                new Contracts.SaveGradingCriterionWizardDto(
                    null, "WRIT", "W_TR", "W_TR", 6m, 1m, 1, "Prose for W_TR", Grid(), Anchors())
            ]));

        await using var check = DbContextFactory.Create(database.ConnectionString);
        var criterionId = await check.GradingCriteria
            .Where(c => c.Cert.Code == certCode).Select(c => c.CriterionId).SingleAsync();
        var stored = await check.GradingCriterionAnchors
            .Where(a => a.CriterionId == criterionId).OrderBy(a => a.Band).ToListAsync();

        Assert.Equal([2, 3, 4], stored.Select(a => a.Band));
        Assert.All(stored, a => Assert.Contains($"band {a.Band} answer", a.ResponseText, StringComparison.Ordinal));
    }

    [Fact]
    public void An_anchor_set_missing_a_calibrated_band_is_refused_before_anything_is_written_and_names_the_band()
    {
        // The mutation first: bands 2 and 4 authored, band 3 absent. A guard written as "has anchors"
        // passes it, and the rubric is graded with a floor and a ceiling and no middle - which is where
        // a fluent but empty answer sits, so the compression the anchors exist to stop is exactly the
        // gap left open.
        var certCode = Unique("AW07");
        var partial = Anchors().Where(a => a.Band != 3).ToList();

        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(
                Request(certCode, "WRIT",
                [
                    new Contracts.SaveGradingCriterionWizardDto(
                        null, "WRIT", "W_TR", "W_TR", 6m, 1m, 1, "Prose for W_TR", Grid(), partial)
                ])));

        Assert.Contains("W_TR", error.Message, StringComparison.Ordinal);
        Assert.Contains("band(s) 3", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void An_anchor_at_a_band_outside_two_to_four_is_refused_by_the_validator_too()
    {
        // The schema CHECK refuses band 5. The validator refuses it first so the author gets a reason
        // naming the rule rather than a constraint name from a save.
        var certCode = Unique("AW08");
        var invalid = Anchors()
            .Append(new Contracts.SaveGradingCriterionAnchorDto(5, "A band five anchor that no call will ever send."))
            .ToList();

        var error = Assert.Throws<ScoreConversionException>(() =>
            CertificateConfigurationValidator.Validate(
                Request(certCode, "WRIT",
                [
                    new Contracts.SaveGradingCriterionWizardDto(
                        null, "WRIT", "W_TR", "W_TR", 6m, 1m, 1, "Prose for W_TR", Grid(), invalid)
                ])));

        Assert.Contains("outside 2-4", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_save_that_omits_anchors_leaves_the_stored_anchors_alone()
    {
        // The same rule as the grid: a form that posts criteria without anchors must not silently
        // unanchor a rubric, which would leave a locked-or-lockable rubric with no benchmark. Both an
        // absent collection AND an empty one mean "not authored here" - an empty one is the shape a form
        // actually posts, and a guard written as `Anchors is not null` passes it and deletes all three.
        var certCode = Unique("AW09");
        await using var db = DbContextFactory.Create(database.ConnectionString);

        await new CertificateService(db).SaveCertificateWizardAsync(
            Request(certCode, "WRIT",
            [
                new Contracts.SaveGradingCriterionWizardDto(
                    null, "WRIT", "W_TR", "W_TR", 6m, 1m, 1, "Prose for W_TR", Grid(), Anchors())
            ]));

        var certId = await db.Certificates.Where(c => c.Code == certCode).Select(c => c.CertId).SingleAsync();
        var criterionId = await db.GradingCriteria
            .Where(c => c.Cert.Code == certCode).Select(c => c.CriterionId).SingleAsync();

        // null: the JSON body omits the field entirely
        await new CertificateService(db).SaveCertificateWizardAsync(
            Request(certCode, "WRIT", [Criterion("WRIT", "W_TR", 1, null)]) with { CertId = certId });

        await using (var check = DbContextFactory.Create(database.ConnectionString))
        {
            Assert.Equal(3, await check.GradingCriterionAnchors.CountAsync(a => a.CriterionId == criterionId));
        }

        // []: the body carries an empty array, which is what an unmodified form posts
        await new CertificateService(db).SaveCertificateWizardAsync(
            Request(certCode, "WRIT",
            [
                new Contracts.SaveGradingCriterionWizardDto(
                    criterionId, "WRIT", "W_TR", "W_TR", 6m, 1m, 1, "Prose for W_TR", null, [])
            ]) with { CertId = certId });

        await using var final = DbContextFactory.Create(database.ConnectionString);
        Assert.Equal(3, await final.GradingCriterionAnchors.CountAsync(a => a.CriterionId == criterionId));
    }

    [Fact]
    public async Task An_anchor_cannot_be_written_after_the_rubric_is_locked_which_is_why_it_is_authored_before()
    {
        // The order the ticket names: anchors are authorable BEFORE the lock, not after. The schema is
        // where that is enforced; this asserts it from the application side so a save that reorders the
        // two steps fails loudly rather than silently dropping the anchors.
        var certCode = Unique("AW10");
        await using var db = DbContextFactory.Create(database.ConnectionString);

        var saved = await new CertificateService(db).SaveCertificateWizardAsync(
            Request(certCode, "WRIT",
                CanonicalWriting.Select((c, i) => Criterion("WRIT", c, i + 1, Grid())).ToList()));

        var skillId = await db.Skills.Where(s => s.Cert.Code == certCode).Select(s => s.SkillId).SingleAsync();
        db.GradingRubricLocks.Add(new GradingRubricLock { SkillId = skillId, CertId = saved.CertId });
        await db.SaveChangesAsync();

        var criterionId = await db.GradingCriteria
            .Where(c => c.Cert.Code == certCode).Select(c => c.CriterionId).FirstAsync();
        db.GradingCriterionAnchors.Add(new GradingCriterionAnchor
        {
            CriterionId = criterionId,
            Band = 2,
            ResponseText = "A band two anchor written after the lock.",
        });

        var error = await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Contains("locked", Flatten(error), StringComparison.OrdinalIgnoreCase);
    }
}
