using System.Text;
using BusinessObjects;
using Contracts;
using DataAccess;
using DataAccess.Scaffold;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface ICertificateService
{
    Task<IReadOnlyList<CertificateCatalog>> GetCatalogAsync(CancellationToken cancellationToken = default);

    IReadOnlyList<CertificateBlueprintPresetDto> GetOfficialPresets();

    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, CancellationToken cancellationToken = default);

    Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(string code, CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(CancellationToken cancellationToken = default);

    Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, CancellationToken cancellationToken = default);
}

public sealed class CertificateService(CompanionDbContext db) : ICertificateService
{
    public async Task<IReadOnlyList<CertificateCatalog>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        var certificates = await db.Certificates
            .AsNoTracking()
            .Where(c => c.IsActive)
            .OrderBy(c => c.CertId)
            .ToListAsync(cancellationToken);

        var certIds = certificates.Select(c => c.CertId).ToList();

        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => certIds.Contains(s.CertId))
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var skillIds = skills.Select(s => s.SkillId).ToList();

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => skillIds.Contains(s.SkillId))
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var bands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => certIds.Contains(b.CertId))
            .OrderBy(b => b.DisplayOrder)
            .ToListAsync(cancellationToken);

        return certificates
            .Select(c => new CertificateCatalog(
                c.Code,
                c.Name,
                c.Description,
                skills.Where(s => s.CertId == c.CertId)
                    .Select(s => new SkillCatalog(
                        s.Code,
                        s.Name,
                        EnumCodes.Parse<Modality>(s.Modality),
                        s.DurationMinutes,
                        sections.Where(sec => sec.SkillId == s.SkillId)
                            .Select(sec => new SectionCatalog(
                                sec.Code,
                                sec.Name,
                                sec.ExpectedQuestionCount,
                                sec.SectionId,
                                sec.NavigationMode,
                                sec.ScoreWeight,
                                sec.TimerPolicyJson,
                                sec.LayoutType,
                                sec.OptionCount,
                                sec.ClusterSize,
                                sec.UnitCount,
                                sec.HideOptionText,
                                sec.RequirePassage,
                                sec.RequireAudio,
                                sec.RequireImage,
                                sec.RequiredKeywordCount,
                                sec.AllowedComponentTypesJson,
                                sec.ToolsConfigJson,
                                sec.DisplayOrder)).ToList(),
                        s.SkillId,
                        s.DisplayOrder)).ToList(),
                bands.Where(b => b.CertId == c.CertId)
                    .Select(b => new BandCatalog(
                        b.Code,
                        b.Name,
                        b.MinTotal,
                        b.MaxTotal)).ToList(),
                c.CertId,
                c.ScoreAggregationRule,
                c.PresetCode,
                c.IsActive))
            .ToList();
    }

    public IReadOnlyList<CertificateBlueprintPresetDto> GetOfficialPresets()
    {
        return
        [
            // 1. IELTS Academic
            new CertificateBlueprintPresetDto(
                PresetCode: "ielts_academic",
                Name: "IELTS Academic",
                Description: "International English Language Testing System (Academic) - 4 skills, 0.5 step scoring (0.0 to 9.0). Listening features audio preview and 2-minute post-audio review countdown; Reading and Writing use horizontal split-screen; Writing weights Task 2 at 2:1; Speaking is auto-paced.",
                ScoreAggregationRule: "avg_half_step",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "LIST",
                        Name: "Listening",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 30,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "REC",
                                Name: "Four recordings: everyday conversation to academic lecture",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.AudioDriven,
                                TimerPolicyJson: "{\"timerMode\":\"AUDIO_PLUS_CHECK\",\"reviewSeconds\":120}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: true,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 40,
                                AllowedComponentTypesJson: null,
                                ToolsConfigJson: "{\"highlighter\":true,\"notes\":true}")
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "READ",
                        Name: "Reading",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 2,
                        DurationMinutes: 60,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "PAS",
                                Name: "Three long passages with a mix of question types",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.FullSandbox,
                                TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: true,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 40,
                                AllowedComponentTypesJson: null,
                                ToolsConfigJson: "{\"highlighter\":true,\"notes\":true}")
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "WRIT",
                        Name: "Writing",
                        Modality: ModalityDto.Writing,
                        DisplayOrder: 3,
                        DurationMinutes: 60,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "T1",
                                Name: "Task 1 — Describe a chart (150 words)",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.FullSandbox,
                                TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: false,
                                RequireImage: true,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 1,
                                AllowedComponentTypesJson: "[\"free_response\"]",
                                ToolsConfigJson: "{\"wordCount\":true,\"disableSpellcheck\":true}"),
                            new SectionBlueprintPresetDto(
                                Code: "T2",
                                Name: "Task 2 — Write an essay (250 words)",
                                DisplayOrder: 2,
                                ScoreWeight: 2.0m,
                                NavigationMode: NavigationModeDto.FullSandbox,
                                TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
                                LayoutType: LayoutTypeDto.SplitHorizontal,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 1,
                                AllowedComponentTypesJson: "[\"free_response\"]",
                                ToolsConfigJson: "{\"wordCount\":true,\"disableSpellcheck\":true}")
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "SPEA",
                        Name: "Speaking",
                        Modality: ModalityDto.Speaking,
                        DisplayOrder: 4,
                        DurationMinutes: 14,
                        Sections:
                        [
                            new SectionBlueprintPresetDto(
                                Code: "INT",
                                Name: "Interview, cue-card talk and discussion",
                                DisplayOrder: 1,
                                ScoreWeight: 1.0m,
                                NavigationMode: NavigationModeDto.SystemPacedAuto,
                                TimerPolicyJson: "{\"timerMode\":\"ITEM_FIXED\"}",
                                LayoutType: LayoutTypeDto.StandardVertical,
                                OptionCount: 4,
                                ClusterSize: 1,
                                UnitCount: 1,
                                HideOptionText: false,
                                RequirePassage: false,
                                RequireAudio: false,
                                RequireImage: false,
                                RequiredKeywordCount: 0,
                                ExpectedQuestionCount: 3,
                                AllowedComponentTypesJson: "[\"free_response\"]",
                                ToolsConfigJson: null)
                        ])
                ]),

            // 2. TOEIC Listening & Reading (Standard CBT)
            new CertificateBlueprintPresetDto(
                PresetCode: "toeic_lr_std",
                Name: "TOEIC Listening & Reading (Standard CBT)",
                Description: "Standard 200 questions across 7 parts. Listening is audio-driven with option text hidden for Parts 1 & 2, and 3-question clusters for Parts 3 & 4. Reading is full sandbox single-column.",
                ScoreAggregationRule: "sum",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "LIST",
                        Name: "Listening",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 45,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("P1", "Part 1: Photographs", 1, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, true, false, true, true, 0, 6, "[\"mcq\"]", null),
                            new SectionBlueprintPresetDto("P2", "Part 2: Question-Response", 2, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 3, 1, 1, true, false, true, false, 0, 25, "[\"mcq_3\"]", null),
                            new SectionBlueprintPresetDto("P3", "Part 3: Conversations", 3, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 3, 1, false, false, true, false, 0, 39, "[\"mcq\"]", null),
                            new SectionBlueprintPresetDto("P4", "Part 4: Talks", 4, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 3, 1, false, false, true, false, 0, 30, "[\"mcq\"]", null)
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "READ",
                        Name: "Reading",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 2,
                        DurationMinutes: 75,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("P5", "Part 5: Incomplete Sentences", 1, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 30, "[\"mcq\"]", null),
                            new SectionBlueprintPresetDto("P6", "Part 6: Text Completion", 2, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 4, 1, false, true, false, false, 0, 16, "[\"mcq\"]", null),
                            new SectionBlueprintPresetDto("P7", "Part 7: Reading Comprehension", 3, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, true, false, false, 0, 54, "[\"mcq\"]", null)
                        ])
                ]),

            // 3. TOEIC Listening & Reading (Adaptive CAT)
            new CertificateBlueprintPresetDto(
                PresetCode: "toeic_lr_cat",
                Name: "TOEIC Listening & Reading (Adaptive CAT)",
                Description: "Two-stage computer adaptive test. Listening is audio-driven. Reading uses unit locking (UNIT_LOCKED_SANDBOX, UnitCount = 2) where candidates cannot return to Unit 1 once advancing to Unit 2.",
                ScoreAggregationRule: "sum",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "LIST",
                        Name: "Listening",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 45,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("LIST_CAT", "Listening Sections (Parts 1-4)", 1, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, true, false, 0, 50, "[\"mcq\",\"mcq_3\"]", null)
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "READ",
                        Name: "Reading",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 2,
                        DurationMinutes: 45,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("READ_CAT", "Adaptive Reading (Unit 1 & Unit 2)", 1, 1.0m, NavigationModeDto.UnitLockedSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 2, false, true, false, false, 0, 50, "[\"mcq\"]", null)
                        ])
                ]),

            // 4. TOEIC Speaking & Writing
            new CertificateBlueprintPresetDto(
                PresetCode: "toeic_sw",
                Name: "TOEIC Speaking & Writing",
                Description: "Speaking consists of 11 auto-paced questions. Writing operates in hybrid group-locked mode with 5 picture-based sentence questions requiring 2 keywords, 2 written requests, and 1 opinion essay with on-screen cut/paste tools.",
                ScoreAggregationRule: "independent",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "SPEA",
                        Name: "Speaking",
                        Modality: ModalityDto.Speaking,
                        DisplayOrder: 1,
                        DurationMinutes: 20,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("S1", "Questions 1-2: Read a text aloud", 1, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 2, "[\"free_response\"]", null),
                            new SectionBlueprintPresetDto("S2", "Questions 3-4: Describe a picture", 2, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, true, 0, 2, "[\"free_response\"]", null),
                            new SectionBlueprintPresetDto("S3", "Questions 5-7: Respond to questions", 3, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, true, false, 0, 3, "[\"free_response\"]", null),
                            new SectionBlueprintPresetDto("S4", "Questions 8-10: Respond using information provided", 4, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, true, true, false, 0, 3, "[\"free_response\"]", null),
                            new SectionBlueprintPresetDto("S5", "Question 11: Express an opinion", 5, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, true, false, 0, 1, "[\"free_response\"]", null)
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "WRIT",
                        Name: "Writing",
                        Modality: ModalityDto.Writing,
                        DisplayOrder: 2,
                        DurationMinutes: 60,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("W1", "Questions 1-5: Write a sentence based on a picture", 1, 1.0m, NavigationModeDto.HybridGroupLocked, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, true, 2, 5, "[\"free_response\"]", "{\"cutPaste\":true,\"wordCount\":true,\"disableSpellcheck\":true}"),
                            new SectionBlueprintPresetDto("W2", "Questions 6-7: Respond to a written request", 2, 1.0m, NavigationModeDto.HybridGroupLocked, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, true, false, false, 0, 2, "[\"free_response\"]", "{\"cutPaste\":true,\"wordCount\":true,\"disableSpellcheck\":true}"),
                            new SectionBlueprintPresetDto("W3", "Question 8: Write an opinion essay", 3, 2.0m, NavigationModeDto.HybridGroupLocked, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 1, "[\"free_response\"]", "{\"cutPaste\":true,\"wordCount\":true,\"disableSpellcheck\":true}")
                        ])
                ]),

            // 5. VSTEP (B1-B2-C1)
            new CertificateBlueprintPresetDto(
                PresetCode: "vstep",
                Name: "VSTEP (B1-B2-C1)",
                Description: "Vietnamese Standardized Test of English Proficiency (Levels 3 to 5 / B1 to C1). Listening is audio-driven; Reading and Writing use horizontal split-screen; Speaking is auto-paced. Scoring aggregated to average whole band.",
                ScoreAggregationRule: "avg_whole",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "LIST",
                        Name: "Listening",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 40,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("VSTEP_LIST", "Listening Parts 1-3", 1, 1.0m, NavigationModeDto.AudioDriven, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, true, false, 0, 35, "[\"mcq\"]", null)
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "READ",
                        Name: "Reading",
                        Modality: ModalityDto.Reading,
                        DisplayOrder: 2,
                        DurationMinutes: 60,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("VSTEP_READ", "Reading Passages 1-4", 1, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.SplitHorizontal, 4, 1, 1, false, true, false, false, 0, 40, "[\"mcq\"]", "{\"highlighter\":true,\"notes\":true}")
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "WRIT",
                        Name: "Writing",
                        Modality: ModalityDto.Writing,
                        DisplayOrder: 3,
                        DurationMinutes: 60,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("VSTEP_WRIT", "Writing Tasks 1 & 2", 1, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.SplitHorizontal, 4, 1, 1, false, false, false, false, 0, 2, "[\"free_response\"]", "{\"wordCount\":true,\"disableSpellcheck\":true}")
                        ]),
                    new SkillBlueprintPresetDto(
                        Code: "SPEA",
                        Name: "Speaking",
                        Modality: ModalityDto.Speaking,
                        DisplayOrder: 4,
                        DurationMinutes: 12,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("VSTEP_SPEA", "Speaking Parts 1-3", 1, 1.0m, NavigationModeDto.SystemPacedAuto, "{\"timerMode\":\"ITEM_FIXED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 3, "[\"free_response\"]", null)
                        ])
                ]),

            // 6. Custom Template
            new CertificateBlueprintPresetDto(
                PresetCode: "custom",
                Name: "Custom Certificate",
                Description: "Blank template to configure custom skills, sections, navigation modes, timers, and authoring rules.",
                ScoreAggregationRule: "sum",
                Skills:
                [
                    new SkillBlueprintPresetDto(
                        Code: "SKILL1",
                        Name: "Section/Skill 1",
                        Modality: ModalityDto.Listening,
                        DisplayOrder: 1,
                        DurationMinutes: 30,
                        Sections:
                        [
                            new SectionBlueprintPresetDto("SEC1", "Section 1", 1, 1.0m, NavigationModeDto.FullSandbox, "{\"timerMode\":\"SECTION_SHARED\"}", LayoutTypeDto.StandardVertical, 4, 1, 1, false, false, false, false, 0, 10, null, null)
                        ])
                ])
        ];
    }

    public async Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(int certId, CancellationToken cancellationToken = default)
    {
        var cert = await db.Certificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.CertId == certId, cancellationToken);

        if (cert is null) return null;

        return await LoadBlueprintDetailsAsync(cert, cancellationToken);
    }

    public async Task<CertificateBlueprintDetailDto?> GetCertificateBlueprintAsync(string code, CancellationToken cancellationToken = default)
    {
        var cert = await db.Certificates
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Code == code, cancellationToken);

        if (cert is null) return null;

        return await LoadBlueprintDetailsAsync(cert, cancellationToken);
    }

    private async Task<CertificateBlueprintDetailDto> LoadBlueprintDetailsAsync(Certificate cert, CancellationToken cancellationToken)
    {
        var skills = await db.Skills
            .AsNoTracking()
            .Where(s => s.CertId == cert.CertId)
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var skillIds = skills.Select(s => s.SkillId).ToList();

        var sections = await db.Sections
            .AsNoTracking()
            .Where(s => skillIds.Contains(s.SkillId))
            .OrderBy(s => s.DisplayOrder)
            .ToListAsync(cancellationToken);

        var bands = await db.ScoreBands
            .AsNoTracking()
            .Where(b => b.CertId == cert.CertId)
            .OrderBy(b => b.DisplayOrder)
            .ToListAsync(cancellationToken);

        return new CertificateBlueprintDetailDto(
            cert.CertId,
            cert.Code,
            cert.Name,
            cert.Description,
            cert.ScoreAggregationRule,
            cert.PresetCode,
            cert.IsActive,
            skills.Select(s => new SkillBlueprintDetailDto(
                s.SkillId,
                s.CertId,
                s.Code,
                s.Name,
                EnumCodes.TryParse<Modality>(s.Modality, out var mod) ? (ModalityDto)mod : ModalityDto.Listening,
                s.DisplayOrder,
                s.DurationMinutes,
                sections.Where(sec => sec.SkillId == s.SkillId).Select(sec => new SectionBlueprintDetailDto(
                    sec.SectionId,
                    sec.SkillId,
                    sec.CertId,
                    sec.Code,
                    sec.Name,
                    sec.DisplayOrder,
                    sec.ScoreWeight,
                    ParseNavigationMode(sec.NavigationMode),
                    sec.TimerPolicyJson,
                    ParseLayoutType(sec.LayoutType),
                    sec.OptionCount,
                    sec.ClusterSize,
                    sec.UnitCount,
                    sec.HideOptionText,
                    sec.RequirePassage,
                    sec.RequireAudio,
                    sec.RequireImage,
                    sec.RequiredKeywordCount,
                    sec.ExpectedQuestionCount,
                    sec.AllowedComponentTypesJson,
                    sec.ToolsConfigJson)).ToList())).ToList(),
            bands.Select(b => new BandDto(b.Code, b.Name, b.MinTotal, b.MaxTotal)).ToList());
    }

    public async Task<IReadOnlyList<AdminCertificateSummaryDto>> GetAdminCertificatesAsync(CancellationToken cancellationToken = default)
    {
        var certs = await db.Certificates
            .AsNoTracking()
            .OrderBy(c => c.CertId)
            .ToListAsync(cancellationToken);

        var certIds = certs.Select(c => c.CertId).ToList();

        var skillCounts = await db.Skills
            .AsNoTracking()
            .Where(s => certIds.Contains(s.CertId))
            .GroupBy(s => s.CertId)
            .Select(g => new { CertId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CertId, x => x.Count, cancellationToken);

        var sectionCounts = await db.Sections
            .AsNoTracking()
            .Where(s => certIds.Contains(s.CertId))
            .GroupBy(s => s.CertId)
            .Select(g => new { CertId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CertId, x => x.Count, cancellationToken);

        var examCounts = await db.Exams
            .AsNoTracking()
            .Where(e => certIds.Contains(e.CertId))
            .GroupBy(e => e.CertId)
            .Select(g => new { CertId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.CertId, x => x.Count, cancellationToken);

        return certs.Select(c => new AdminCertificateSummaryDto(
            c.CertId,
            c.Code,
            c.Name,
            c.Description,
            c.ScoreAggregationRule,
            c.PresetCode,
            c.IsActive,
            skillCounts.GetValueOrDefault(c.CertId, 0),
            sectionCounts.GetValueOrDefault(c.CertId, 0),
            examCounts.GetValueOrDefault(c.CertId, 0))).ToList();
    }

    public async Task<SaveCertificateWizardResultDto> SaveCertificateWizardAsync(SaveCertificateWizardRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.Code))
            throw new ArgumentException("Certificate code is required.", nameof(request));

        if (string.IsNullOrWhiteSpace(request.Name))
            throw new ArgumentException("Certificate name is required.", nameof(request));

        var certCode = request.Code.Trim().ToUpperInvariant();
        var scoreRule = string.IsNullOrWhiteSpace(request.ScoreAggregationRule)
            ? "sum"
            : request.ScoreAggregationRule.Trim().ToLowerInvariant();

        if (scoreRule is not ("sum" or "avg_half_step" or "avg_whole" or "independent"))
            throw new ArgumentException($"Invalid ScoreAggregationRule '{request.ScoreAggregationRule}'. Must be sum, avg_half_step, avg_whole, or independent.", nameof(request));

        if (request.Skills is null || request.Skills.Count == 0)
            throw new ArgumentException("Certificate must define at least one skill.", nameof(request));

        Certificate cert;
        if (request.CertId.HasValue && request.CertId.Value > 0)
        {
            cert = await db.Certificates
                .FirstOrDefaultAsync(c => c.CertId == request.CertId.Value, cancellationToken)
                ?? throw new KeyNotFoundException($"Certificate with ID {request.CertId.Value} was not found.");

            cert.Name = request.Name.Trim();
            cert.Description = request.Description?.Trim();
            cert.ScoreAggregationRule = scoreRule;
            cert.PresetCode = request.PresetCode;
            cert.IsActive = request.IsActive;
        }
        else
        {
            var existingWithCode = await db.Certificates
                .FirstOrDefaultAsync(c => c.Code == certCode, cancellationToken);

            if (existingWithCode is not null)
                throw new ArgumentException($"Certificate with code '{certCode}' already exists.", nameof(request));

            cert = new Certificate
            {
                Code = certCode,
                Name = request.Name.Trim(),
                Description = request.Description?.Trim(),
                ScoreAggregationRule = scoreRule,
                PresetCode = request.PresetCode,
                IsActive = request.IsActive
            };
            db.Certificates.Add(cert);
            await db.SaveChangesAsync(cancellationToken);
        }

        var totalSkills = 0;
        var totalSections = 0;

        foreach (var skillDto in request.Skills)
        {
            if (string.IsNullOrWhiteSpace(skillDto.Code))
                throw new ArgumentException("Skill code is required.", nameof(request));
            if (string.IsNullOrWhiteSpace(skillDto.Name))
                throw new ArgumentException("Skill name is required.", nameof(request));

            var skillCode = skillDto.Code.Trim().ToUpperInvariant();
            var modalityStr = skillDto.Modality.ToString().ToLowerInvariant();

            Skill skill;
            if (skillDto.SkillId.HasValue && skillDto.SkillId.Value > 0)
            {
                skill = await db.Skills
                    .FirstOrDefaultAsync(s => s.SkillId == skillDto.SkillId.Value && s.CertId == cert.CertId, cancellationToken)
                    ?? throw new KeyNotFoundException($"Skill with ID {skillDto.SkillId.Value} not found for certificate {cert.CertId}.");

                skill.Name = skillDto.Name.Trim();
                skill.Modality = modalityStr;
                skill.DisplayOrder = skillDto.DisplayOrder;
                skill.DurationMinutes = skillDto.DurationMinutes;
            }
            else
            {
                var existingSkill = await db.Skills
                    .FirstOrDefaultAsync(s => s.CertId == cert.CertId && s.Code == skillCode, cancellationToken);

                if (existingSkill is not null)
                {
                    skill = existingSkill;
                    skill.Name = skillDto.Name.Trim();
                    skill.Modality = modalityStr;
                    skill.DisplayOrder = skillDto.DisplayOrder;
                    skill.DurationMinutes = skillDto.DurationMinutes;
                }
                else
                {
                    skill = new Skill
                    {
                        CertId = cert.CertId,
                        Code = skillCode,
                        Name = skillDto.Name.Trim(),
                        Modality = modalityStr,
                        DisplayOrder = skillDto.DisplayOrder,
                        DurationMinutes = skillDto.DurationMinutes
                    };
                    db.Skills.Add(skill);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            totalSkills++;

            if (skillDto.Sections is null || skillDto.Sections.Count == 0)
                throw new ArgumentException($"Skill '{skillCode}' must have at least one section.", nameof(request));

            foreach (var secDto in skillDto.Sections)
            {
                if (string.IsNullOrWhiteSpace(secDto.Code))
                    throw new ArgumentException($"Section code is required for skill '{skillCode}'.", nameof(request));
                if (string.IsNullOrWhiteSpace(secDto.Name))
                    throw new ArgumentException($"Section name is required for section '{secDto.Code}'.", nameof(request));

                var secCode = secDto.Code.Trim().ToUpperInvariant();
                var navModeStr = FormatUpperSnakeCase(secDto.NavigationMode.ToString());
                var layoutStr = FormatUpperSnakeCase(secDto.LayoutType.ToString());

                Section section;
                if (secDto.SectionId.HasValue && secDto.SectionId.Value > 0)
                {
                    section = await db.Sections
                        .FirstOrDefaultAsync(sec => sec.SectionId == secDto.SectionId.Value && sec.CertId == cert.CertId, cancellationToken)
                        ?? throw new KeyNotFoundException($"Section with ID {secDto.SectionId.Value} not found for certificate {cert.CertId}.");

                    section.Name = secDto.Name.Trim();
                    section.Modality = modalityStr;
                    section.DisplayOrder = secDto.DisplayOrder;
                    section.ScoreWeight = secDto.ScoreWeight > 0 ? secDto.ScoreWeight : 1.0m;
                    section.NavigationMode = navModeStr;
                    section.TimerPolicyJson = secDto.TimerPolicyJson;
                    section.LayoutType = layoutStr;
                    section.OptionCount = secDto.OptionCount > 0 ? secDto.OptionCount : 4;
                    section.ClusterSize = secDto.ClusterSize > 0 ? secDto.ClusterSize : 1;
                    section.UnitCount = secDto.UnitCount > 0 ? secDto.UnitCount : 1;
                    section.HideOptionText = secDto.HideOptionText;
                    section.RequirePassage = secDto.RequirePassage;
                    section.RequireAudio = secDto.RequireAudio;
                    section.RequireImage = secDto.RequireImage;
                    section.RequiredKeywordCount = secDto.RequiredKeywordCount;
                    section.ExpectedQuestionCount = secDto.ExpectedQuestionCount;
                    section.AllowedComponentTypesJson = secDto.AllowedComponentTypesJson;
                    section.ToolsConfigJson = secDto.ToolsConfigJson;
                }
                else
                {
                    var existingSec = await db.Sections
                        .FirstOrDefaultAsync(sec => sec.SkillId == skill.SkillId && sec.Code == secCode, cancellationToken);

                    if (existingSec is not null)
                    {
                        section = existingSec;
                        section.Name = secDto.Name.Trim();
                        section.Modality = modalityStr;
                        section.DisplayOrder = secDto.DisplayOrder;
                        section.ScoreWeight = secDto.ScoreWeight > 0 ? secDto.ScoreWeight : 1.0m;
                        section.NavigationMode = navModeStr;
                        section.TimerPolicyJson = secDto.TimerPolicyJson;
                        section.LayoutType = layoutStr;
                        section.OptionCount = secDto.OptionCount > 0 ? secDto.OptionCount : 4;
                        section.ClusterSize = secDto.ClusterSize > 0 ? secDto.ClusterSize : 1;
                        section.UnitCount = secDto.UnitCount > 0 ? secDto.UnitCount : 1;
                        section.HideOptionText = secDto.HideOptionText;
                        section.RequirePassage = secDto.RequirePassage;
                        section.RequireAudio = secDto.RequireAudio;
                        section.RequireImage = secDto.RequireImage;
                        section.RequiredKeywordCount = secDto.RequiredKeywordCount;
                        section.ExpectedQuestionCount = secDto.ExpectedQuestionCount;
                        section.AllowedComponentTypesJson = secDto.AllowedComponentTypesJson;
                        section.ToolsConfigJson = secDto.ToolsConfigJson;
                    }
                    else
                    {
                        section = new Section
                        {
                            SkillId = skill.SkillId,
                            CertId = cert.CertId,
                            Modality = modalityStr,
                            Code = secCode,
                            Name = secDto.Name.Trim(),
                            DisplayOrder = secDto.DisplayOrder,
                            ScoreWeight = secDto.ScoreWeight > 0 ? secDto.ScoreWeight : 1.0m,
                            NavigationMode = navModeStr,
                            TimerPolicyJson = secDto.TimerPolicyJson,
                            LayoutType = layoutStr,
                            OptionCount = secDto.OptionCount > 0 ? secDto.OptionCount : 4,
                            ClusterSize = secDto.ClusterSize > 0 ? secDto.ClusterSize : 1,
                            UnitCount = secDto.UnitCount > 0 ? secDto.UnitCount : 1,
                            HideOptionText = secDto.HideOptionText,
                            RequirePassage = secDto.RequirePassage,
                            RequireAudio = secDto.RequireAudio,
                            RequireImage = secDto.RequireImage,
                            RequiredKeywordCount = secDto.RequiredKeywordCount,
                            ExpectedQuestionCount = secDto.ExpectedQuestionCount,
                            AllowedComponentTypesJson = secDto.AllowedComponentTypesJson,
                            ToolsConfigJson = secDto.ToolsConfigJson
                        };
                        db.Sections.Add(section);
                    }
                }

                totalSections++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        return new SaveCertificateWizardResultDto(
            cert.CertId,
            cert.Code,
            cert.Name,
            totalSkills,
            totalSections);
    }

    private static string FormatUpperSnakeCase(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return input;
        var sb = new StringBuilder();
        for (int i = 0; i < input.Length; i++)
        {
            var ch = input[i];
            if (char.IsUpper(ch) && i > 0 && !char.IsUpper(input[i - 1]) && input[i - 1] != '_')
            {
                sb.Append('_');
            }
            sb.Append(char.ToUpperInvariant(ch));
        }
        return sb.ToString();
    }

    private static NavigationModeDto ParseNavigationMode(string? mode)
    {
        if (string.IsNullOrWhiteSpace(mode)) return NavigationModeDto.FullSandbox;
        var normalized = mode.Replace("_", "").ToLowerInvariant();
        return normalized switch
        {
            "unitlockedsandbox" => NavigationModeDto.UnitLockedSandbox,
            "audiodriven" => NavigationModeDto.AudioDriven,
            "systempacedauto" => NavigationModeDto.SystemPacedAuto,
            "hybridgrouplocked" => NavigationModeDto.HybridGroupLocked,
            _ => NavigationModeDto.FullSandbox
        };
    }

    private static LayoutTypeDto ParseLayoutType(string? layout)
    {
        if (string.IsNullOrWhiteSpace(layout)) return LayoutTypeDto.StandardVertical;
        var normalized = layout.Replace("_", "").ToLowerInvariant();
        return normalized switch
        {
            "splithorizontal" => LayoutTypeDto.SplitHorizontal,
            "mediacard" => LayoutTypeDto.MediaCard,
            "cluster3" => LayoutTypeDto.Cluster3,
            _ => LayoutTypeDto.StandardVertical
        };
    }
}
