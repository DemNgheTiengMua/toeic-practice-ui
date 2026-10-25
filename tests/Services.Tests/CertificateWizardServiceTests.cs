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
    public void GetOfficialPresets_Returns_All_Six_Presets_With_Full_Rules()
    {
        using var db = DbContextFactory.Create(database.ConnectionString);
        var service = new CertificateService(db);

        var presets = service.GetOfficialPresets();

        Assert.Equal(6, presets.Count);
        var codes = presets.Select(p => p.PresetCode).ToList();
        Assert.Contains("ielts_academic", codes);
        Assert.Contains("toeic_lr_std", codes);
        Assert.Contains("toeic_lr_cat", codes);
        Assert.Contains("toeic_sw", codes);
        Assert.Contains("vstep", codes);
        Assert.Contains("custom", codes);

        // Verify IELTS Academic preset
        var ielts = presets.Single(p => p.PresetCode == "ielts_academic");
        Assert.Equal("avg_half_step", ielts.ScoreAggregationRule);
        Assert.Equal(4, ielts.Skills.Count);
        var ieltsListening = ielts.Skills.Single(s => s.Code == "LIST");
        Assert.Equal(NavigationModeDto.AudioDriven, ieltsListening.Sections[0].NavigationMode);
        Assert.Equal(LayoutTypeDto.SplitHorizontal, ieltsListening.Sections[0].LayoutType);
        Assert.Contains("AUDIO_PLUS_CHECK", ieltsListening.Sections[0].TimerPolicyJson!);
        Assert.Contains("highlighter", ieltsListening.Sections[0].ToolsConfigJson!);

        var ieltsWriting = ielts.Skills.Single(s => s.Code == "WRIT");
        Assert.Equal(2, ieltsWriting.Sections.Count);
        Assert.Equal(1.0m, ieltsWriting.Sections[0].ScoreWeight);
        Assert.Equal(2.0m, ieltsWriting.Sections[1].ScoreWeight);
        Assert.Contains("wordCount", ieltsWriting.Sections[0].ToolsConfigJson!);
        Assert.Contains("disableSpellcheck", ieltsWriting.Sections[0].ToolsConfigJson!);

        var ieltsSpeaking = ielts.Skills.Single(s => s.Code == "SPEA");
        Assert.Equal(NavigationModeDto.SystemPacedAuto, ieltsSpeaking.Sections[0].NavigationMode);

        // Verify TOEIC Standard CBT preset
        var toeicStd = presets.Single(p => p.PresetCode == "toeic_lr_std");
        Assert.Equal("sum", toeicStd.ScoreAggregationRule);
        var toeicList = toeicStd.Skills.Single(s => s.Code == "LIST");
        var p1 = toeicList.Sections.Single(s => s.Code == "P1");
        Assert.True(p1.HideOptionText);
        Assert.True(p1.RequireImage);
        var p3 = toeicList.Sections.Single(s => s.Code == "P3");
        Assert.Equal(3, p3.ClusterSize);

        // Verify TOEIC CAT preset
        var toeicCat = presets.Single(p => p.PresetCode == "toeic_lr_cat");
        var readCat = toeicCat.Skills.Single(s => s.Code == "READ");
        Assert.Equal(NavigationModeDto.UnitLockedSandbox, readCat.Sections[0].NavigationMode);
        Assert.Equal(2, readCat.Sections[0].UnitCount);

        // Verify TOEIC S&W preset
        var toeicSw = presets.Single(p => p.PresetCode == "toeic_sw");
        var spea = toeicSw.Skills.Single(s => s.Code == "SPEA");
        Assert.Equal(11, spea.Sections.Sum(s => s.ExpectedQuestionCount ?? 0));
        var writ = toeicSw.Skills.Single(s => s.Code == "WRIT");
        var w1 = writ.Sections.Single(s => s.Code == "W1");
        Assert.Equal(NavigationModeDto.HybridGroupLocked, w1.NavigationMode);
        Assert.Equal(2, w1.RequiredKeywordCount);
        Assert.Contains("cutPaste", w1.ToolsConfigJson!);

        // Verify VSTEP preset
        var vstep = presets.Single(p => p.PresetCode == "vstep");
        Assert.Equal("avg_whole", vstep.ScoreAggregationRule);
        Assert.Equal(4, vstep.Skills.Count);
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
}
