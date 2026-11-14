using System.Text.Json;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

[Collection(DatabaseCollection.Name)]
public sealed class CertificateWizardServiceTests(DatabaseFixture database)
{
    [Fact]
    public void GetOfficialPresets_Returns_TwoSkill_And_FourSkill_Templates()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new CertificateService(db);

        var presets = service.GetOfficialPresets();

        Assert.Equal(2, presets.Count);
        var codes = presets.Select(p => p.PresetCode).ToList();
        Assert.Contains("two_skill", codes);
        Assert.Contains("four_skill", codes);

        var twoSkill = presets.Single(p => p.PresetCode == "two_skill");
        Assert.Equal("sum", twoSkill.ScoreAggregationRule);
        Assert.Equal(2, twoSkill.Skills.Count);
        Assert.Contains(twoSkill.Skills, s => s.Modality == ModalityDto.Listening);
        Assert.Contains(twoSkill.Skills, s => s.Modality == ModalityDto.Reading);

        var fourSkill = presets.Single(p => p.PresetCode == "four_skill");
        Assert.Equal("avg_half_step", fourSkill.ScoreAggregationRule);
        Assert.Equal(4, fourSkill.Skills.Count);
        Assert.Contains(fourSkill.Skills, s => s.Modality == ModalityDto.Listening);
        Assert.Contains(fourSkill.Skills, s => s.Modality == ModalityDto.Reading);
        Assert.Contains(fourSkill.Skills, s => s.Modality == ModalityDto.Writing);
        Assert.Contains(fourSkill.Skills, s => s.Modality == ModalityDto.Speaking);
    }

    [Fact]
    public async Task SaveCertificateWizard_Persists_New_Custom_Certificate_With_6Axis_Rules()
    {
        var databaseName = $"CertWiz_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var service = new CertificateService(db);

            var request = new SaveCertificateWizardRequest(
                CertId: null,
                Code: "CUST_TEST",
                Name: "Custom Certification",
                Description: "Integration test for 6-axis custom certificate",
                ScoreAggregationRule: "avg_whole",
                PresetCode: "custom",
                IsActive: true,
                MinScaledScore: 0,
                MaxScaledScore: 100,
                ScoreStep: 1,
                Bands:
                [
                    new SaveScoreBandWizardDto(null, "FAIL", "Fail", 0, 49, 1),
                    new SaveScoreBandWizardDto(null, "PASS", "Pass", 50, 100, 2)
                ],
                Skills:
                [
                    new SaveSkillWizardDto(
                        SkillId: null,
                        Code: "SK1",
                        Name: "Skill One",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 1,
                        DurationMinutes: 50,
                        Sections:
                        [
                            new SaveSectionWizardDto(
                                SectionId: null,
                                Code: "SEC1A",
                                Name: "Section 1A Split",
                                DisplayOrder: 1,
                                ScoreWeight: 1.5m,
                                NavigationMode: NavigationModeDto.UnitLockedSandbox,
                                TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 2,
                                UnitCount: 2,
                                HideOptionText: true,
                                RequirePassage: true,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 20,
                                AllowedComponentTypesJson: "[\"mcq\",\"inline_completion\"]",
                                ToolsConfigJson: "{\"highlighter\":true,\"notes\":true}")
                        ])
                ]);

            var result = await service.SaveCertificateWizardAsync(request);

            Assert.True(result.CertId > 0);
            Assert.Equal("CUST_TEST", result.Code);
            Assert.Equal(1, result.SkillsCount);
            Assert.Equal(1, result.SectionsCount);

            // Read back via GetCertificateBlueprintAsync
            var blueprint = await service.GetCertificateBlueprintAsync(result.CertId);
            Assert.NotNull(blueprint);
            Assert.Equal("CUST_TEST", blueprint.Code);
            Assert.Equal("avg_whole", blueprint.ScoreAggregationRule);
            Assert.Single(blueprint.Skills);
            var skill = blueprint.Skills[0];
            Assert.Equal("SK1", skill.Code);
            Assert.Equal(ModalityDto.Reading, skill.Modality);
            Assert.Equal(50, skill.DurationMinutes);

            var section = Assert.Single(skill.Sections);
            Assert.Equal("SEC1A", section.Code);
            Assert.Equal(1.5m, section.ScoreWeight);
            Assert.Equal(NavigationModeDto.UnitLockedSandbox, section.NavigationMode);
            Assert.Equal(LayoutTypeDto.SplitHorizontal, section.LayoutType);
            Assert.Equal(4, section.OptionCount);
            Assert.Equal(2, section.ClusterSize);
            Assert.Equal(2, section.UnitCount);
            Assert.True(section.HideOptionText);
            Assert.True(section.RequirePassage);
            Assert.Equal(20, section.ExpectedQuestionCount);
            Assert.Equal("[\"mcq\",\"inline_completion\"]", section.AllowedComponentTypesJson);
            Assert.Equal("{\"highlighter\":true,\"notes\":true}", section.ToolsConfigJson);

            // Verify GetCertificateBlueprintAsync by code
            var blueprintByCode = await service.GetCertificateBlueprintAsync("CUST_TEST");
            Assert.NotNull(blueprintByCode);
            Assert.Equal(blueprint.CertId, blueprintByCode.CertId);

            // Verify GetAdminCertificatesAsync contains the new certificate with counts
            var adminList = await service.GetAdminCertificatesAsync();
            var adminCert = Assert.Single(adminList, c => c.Code == "CUST_TEST");
            Assert.Equal(1, adminCert.SkillsCount);
            Assert.Equal(1, adminCert.SectionsCount);
            Assert.Equal(0, adminCert.ExamsCount);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task SaveCertificateWizard_Updates_Existing_Certificate_And_Blueprint()
    {
        var databaseName = $"CertUpdate_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var service = new CertificateService(db);

            // 1. Create initial cert
            var createReq = new SaveCertificateWizardRequest(
                CertId: null,
                Code: "EDIT_TEST",
                Name: "Initial Name",
                Description: "Initial description",
                ScoreAggregationRule: "sum",
                PresetCode: "custom",
                IsActive: true,
                MinScaledScore: 0,
                MaxScaledScore: 100,
                ScoreStep: 1,
                Bands:
                [
                    new SaveScoreBandWizardDto(null, "FAIL", "Fail", 0, 49, 1),
                    new SaveScoreBandWizardDto(null, "PASS", "Pass", 50, 100, 2)
                ],
                Skills:
                [
                    new SaveSkillWizardDto(
                        SkillId: null,
                        Code: "S1",
                        Name: "Skill 1",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 30,
                        Sections:
                        [
                            new SaveSectionWizardDto(
                                SectionId: null,
                                Code: "SEC1",
                                Name: "Section 1",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.AudioDriven,
                                TimerPolicyJson: null,
                                LayoutType: LayoutTypeDto.StandardVertical,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: true,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 10,
                                AllowedComponentTypesJson: null,
                                ToolsConfigJson: null)
                        ])
                ]);

            var created = await service.SaveCertificateWizardAsync(createReq);
            var originalBlueprint = await service.GetCertificateBlueprintAsync(created.CertId);
            Assert.NotNull(originalBlueprint);

            var skillId = originalBlueprint.Skills[0].SkillId;
            var sectionId = originalBlueprint.Skills[0].Sections[0].SectionId;

            // 2. Update existing cert
            var updateReq = new SaveCertificateWizardRequest(
                CertId: created.CertId,
                Code: "EDIT_TEST",
                Name: "Updated Name",
                Description: "Updated description",
                ScoreAggregationRule: "avg_half_step",
                PresetCode: "ielts_academic",
                IsActive: false,
                MinScaledScore: 0,
                MaxScaledScore: 100,
                ScoreStep: 1,
                Bands:
                [
                    new SaveScoreBandWizardDto(null, "FAIL", "Fail", 0, 49, 1),
                    new SaveScoreBandWizardDto(null, "PASS", "Pass", 50, 100, 2)
                ],
                Skills:
                [
                    new SaveSkillWizardDto(
                        SkillId: skillId,
                        Code: "S1",
                        Name: "Updated Skill 1",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 45,
                        Sections:
                        [
                            new SaveSectionWizardDto(
                                SectionId: sectionId,
                                Code: "SEC1",
                                Name: "Updated Section 1",
                                DisplayOrder: 1,
                                ScoreWeight: 2.0m,
                                NavigationMode: NavigationModeDto.AudioDriven,
                                TimerPolicyJson: "{\"timerMode\":\"AUDIO_PLUS_CHECK\",\"reviewSeconds\":180}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 3,
                                UnitCount: 1,
                                HideOptionText: true,
                                RequirePassage: false,
                                RequireAudio: true,
                                RequireImage: true,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 30,
                                AllowedComponentTypesJson: "[\"mcq\"]",
                                ToolsConfigJson: "{\"highlighter\":true}")
                        ])
                ]);

            var updated = await service.SaveCertificateWizardAsync(updateReq);
            Assert.Equal(created.CertId, updated.CertId);

            var updatedBlueprint = await service.GetCertificateBlueprintAsync(created.CertId);
            Assert.NotNull(updatedBlueprint);
            Assert.Equal("Updated Name", updatedBlueprint.Name);
            Assert.Equal("avg_half_step", updatedBlueprint.ScoreAggregationRule);
            Assert.Equal("ielts_academic", updatedBlueprint.PresetCode);
            Assert.False(updatedBlueprint.IsActive);

            var updatedSection = updatedBlueprint.Skills[0].Sections[0];
            Assert.Equal(2.0m, updatedSection.ScoreWeight);
            Assert.Equal(LayoutTypeDto.SplitHorizontal, updatedSection.LayoutType);
            Assert.Equal(3, updatedSection.ClusterSize);
            Assert.True(updatedSection.HideOptionText);
            Assert.True(updatedSection.RequireImage);
            Assert.Equal(30, updatedSection.ExpectedQuestionCount);
            Assert.Equal("{\"highlighter\":true}", updatedSection.ToolsConfigJson);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task SaveCertificateWizard_Rejects_Invalid_Input()
    {
        var databaseName = $"CertInvalid_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var service = new CertificateService(db);

            // Empty code
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveCertificateWizardAsync(new SaveCertificateWizardRequest(
                CertId: null, Code: "", Name: "Name", Description: null, ScoreAggregationRule: "sum", PresetCode: null, IsActive: true, Skills: [])));

            // Invalid ScoreAggregationRule
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveCertificateWizardAsync(new SaveCertificateWizardRequest(
                CertId: null, Code: "TEST", Name: "Name", Description: null, ScoreAggregationRule: "invalid_rule", PresetCode: null, IsActive: true, Skills: [])));

            // No skills
            await Assert.ThrowsAsync<ArgumentException>(() => service.SaveCertificateWizardAsync(new SaveCertificateWizardRequest(
                CertId: null, Code: "TEST", Name: "Name", Description: null, ScoreAggregationRule: "sum", PresetCode: null, IsActive: true, Skills: [])));
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }

    [Fact]
    public async Task SaveCertificateWizard_Applies_Modality_Defaults_And_Linear_Scales_For_ThreeField_Sections()
    {
        var databaseName = $"CertStreamlined_{Guid.NewGuid():N}";
        var connectionString = await database.CreateFreshDatabaseAsync(databaseName);
        try
        {
            await using var db = DbContextFactory.Create(connectionString);
            var service = new CertificateService(db);

            var request = new SaveCertificateWizardRequest(
                CertId: null,
                Code: "STREAM_4S",
                Name: "Streamlined 4-Skill",
                Description: "3-field sections with flat criteria",
                ScoreAggregationRule: "avg_half_step",
                PresetCode: "four_skill",
                IsActive: true,
                MinScaledScore: 0,
                MaxScaledScore: 9,
                ScoreStep: 1,
                Bands:
                [
                    new SaveScoreBandWizardDto(null, "B1", "Intermediate", 0, 5, 1),
                    new SaveScoreBandWizardDto(null, "B2", "Upper Intermediate", 6, 9, 2),
                ],
                Criteria:
                [
                    new SaveGradingCriterionWizardDto(null, "WRIT", "TR", "Task Response", 9m, 1.0m, 1, "Addresses all parts of the prompt."),
                    new SaveGradingCriterionWizardDto(null, "SPEA", "FLU", "Fluency", 9m, 1.0m, 1, "Speaks smoothly without long pauses."),
                ],
                Skills:
                [
                    new SaveSkillWizardDto(null, "LIST", "Listening", ModalityDto.Listening, 1, 30,
                    [
                        new SaveSectionWizardDto(Code: "L1", Name: "Listening Part 1", ExpectedQuestionCount: 10)
                    ]),
                    new SaveSkillWizardDto(null, "READ", "Reading", ModalityDto.Reading, 2, 60,
                    [
                        new SaveSectionWizardDto(Code: "R1", Name: "Reading Passage 1", ExpectedQuestionCount: 20)
                    ]),
                    new SaveSkillWizardDto(null, "WRIT", "Writing", ModalityDto.Writing, 3, 60,
                    [
                        new SaveSectionWizardDto(Code: "W1", Name: "Writing Essay", ExpectedQuestionCount: 1)
                    ]),
                    new SaveSkillWizardDto(null, "SPEA", "Speaking", ModalityDto.Speaking, 4, 15,
                    [
                        new SaveSectionWizardDto(Code: "S1", Name: "Speaking Interview", ExpectedQuestionCount: 3)
                    ]),
                ]);

            var result = await service.SaveCertificateWizardAsync(request);
            Assert.Equal(4, result.SkillsCount);
            Assert.Equal(4, result.SectionsCount);

            var blueprint = await service.GetCertificateBlueprintAsync(result.CertId);
            Assert.NotNull(blueprint);

            var listSec = blueprint.Skills.Single(s => s.Code == "LIST").Sections[0];
            Assert.Equal(NavigationModeDto.AudioDriven, listSec.NavigationMode);
            Assert.True(listSec.RequireAudio);

            var readSec = blueprint.Skills.Single(s => s.Code == "READ").Sections[0];
            Assert.Equal(NavigationModeDto.FullSandbox, readSec.NavigationMode);
            Assert.Equal(LayoutTypeDto.SplitHorizontal, readSec.LayoutType);
            Assert.True(readSec.RequirePassage);

            var writSec = blueprint.Skills.Single(s => s.Code == "WRIT").Sections[0];
            Assert.Equal("[\"free_response\"]", writSec.AllowedComponentTypesJson);

            var speaSec = blueprint.Skills.Single(s => s.Code == "SPEA").Sections[0];
            Assert.Equal(NavigationModeDto.SystemPacedAuto, speaSec.NavigationMode);
            Assert.Equal("[\"free_response\"]", speaSec.AllowedComponentTypesJson);

            // Linear ScoreScales auto-generated for Listening (0..10 raw -> 0..9 scaled)
            var listSkillId = blueprint.Skills.Single(s => s.Code == "LIST").SkillId;
            var scales = await db.ScoreScales.Where(s => s.CertId == result.CertId && s.SkillId == listSkillId).ToListAsync();
            Assert.Equal(11, scales.Count);
            Assert.Equal(0, scales.Single(s => s.RawScore == 0).ScaledScore);
            Assert.Equal(9, scales.Single(s => s.RawScore == 10).ScaledScore);

            // Flat GradingCriteria persisted with no descriptor/anchor/lock rows
            var criteria = await db.GradingCriteria.Where(c => c.CertId == result.CertId).ToListAsync();
            Assert.Equal(2, criteria.Count);
        }
        finally
        {
            await database.DropDatabaseAsync(databaseName);
        }
    }
}
