using System.Text;
using BusinessObjects;
using Contracts;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public sealed partial class CertificateService
{

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

        if (scoreRule is not ("sum" or "avg_half_step" or "avg_whole"))
            throw new ArgumentException($"Invalid ScoreAggregationRule '{request.ScoreAggregationRule}'. Must be sum, avg_half_step, or avg_whole.", nameof(request));

        if (request.Skills is null || request.Skills.Count == 0)
            throw new ArgumentException("Certificate must define at least one skill.", nameof(request));

        // E1.2 (#147): the wizard is the only place a certificate's scoring is configured, so the
        // invariants are checked here, before anything is written. A certificate that reaches the
        // database with no bands cannot be scored and fails every placement submission.
        CertificateConfigurationValidator.Validate(request);

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
                skill.MinScaledScore = request.MinScaledScore;
                skill.MaxScaledScore = request.MaxScaledScore;
                skill.ScoreStep = request.ScoreStep;
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
                    skill.MinScaledScore = request.MinScaledScore;
                    skill.MaxScaledScore = request.MaxScaledScore;
                    skill.ScoreStep = request.ScoreStep;
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
                        DurationMinutes = skillDto.DurationMinutes,
                        MinScaledScore = request.MinScaledScore,
                        MaxScaledScore = request.MaxScaledScore,
                        ScoreStep = request.ScoreStep
                    };
                    db.Skills.Add(skill);
                    await db.SaveChangesAsync(cancellationToken);
                }
            }

            totalSkills++;

            if (skillDto.Sections is null || skillDto.Sections.Count == 0)
                throw new ArgumentException($"Skill '{skillCode}' must have at least one section.", nameof(request));

            var sectionOrder = 0;
            foreach (var secDto in skillDto.Sections)
            {
                sectionOrder++;
                if (string.IsNullOrWhiteSpace(secDto.Code))
                    throw new ArgumentException($"Section code is required for skill '{skillCode}'.", nameof(request));
                if (string.IsNullOrWhiteSpace(secDto.Name))
                    throw new ArgumentException($"Section name is required for section '{secDto.Code}'.", nameof(request));
                if (secDto.ExpectedQuestionCount <= 0)
                    throw new ArgumentException($"Section '{secDto.Code}' must declare how many questions it holds (ExpectedQuestionCount > 0).", nameof(request));

                var secCode = secDto.Code.Trim().ToUpperInvariant();
                var defaults = ModalityDefaults(modalityStr);
                var displayOrder = secDto.DisplayOrder > 0 ? secDto.DisplayOrder : sectionOrder;
                var navModeStr = FormatUpperSnakeCase((secDto.NavigationMode ?? defaults.NavigationMode).ToString());
                var layoutStr = FormatUpperSnakeCase((secDto.LayoutType ?? defaults.LayoutType).ToString());
                var timerPolicy = secDto.TimerPolicyJson ?? defaults.TimerPolicyJson;
                var requirePassage = secDto.RequirePassage ?? defaults.RequirePassage;
                var requireAudio = secDto.RequireAudio ?? defaults.RequireAudio;
                var allowedTypes = secDto.AllowedComponentTypesJson ?? defaults.AllowedComponentTypesJson;
                var toolsConfig = secDto.ToolsConfigJson ?? defaults.ToolsConfigJson;

                Section section;
                if (secDto.SectionId.HasValue && secDto.SectionId.Value > 0)
                {
                    section = await db.Sections
                        .FirstOrDefaultAsync(sec => sec.SectionId == secDto.SectionId.Value && sec.CertId == cert.CertId, cancellationToken)
                        ?? throw new KeyNotFoundException($"Section with ID {secDto.SectionId.Value} not found for certificate {cert.CertId}.");

                    section.Name = secDto.Name.Trim();
                    section.Modality = modalityStr;
                    section.DisplayOrder = displayOrder;
                    section.ScoreWeight = secDto.ScoreWeight > 0 ? secDto.ScoreWeight : 1.0m;
                    section.NavigationMode = navModeStr;
                    section.TimerPolicyJson = timerPolicy;
                    section.LayoutType = layoutStr;
                    section.OptionCount = secDto.OptionCount > 0 ? secDto.OptionCount : 4;
                    section.ClusterSize = secDto.ClusterSize > 0 ? secDto.ClusterSize : 1;
                    section.UnitCount = secDto.UnitCount > 0 ? secDto.UnitCount : 1;
                    section.HideOptionText = secDto.HideOptionText;
                    section.RequirePassage = requirePassage;
                    section.RequireAudio = requireAudio;
                    section.RequireImage = secDto.RequireImage;
                    section.RequiredKeywordCount = secDto.RequiredKeywordCount;
                    section.ExpectedQuestionCount = secDto.ExpectedQuestionCount;
                    section.AllowedComponentTypesJson = allowedTypes;
                    section.ToolsConfigJson = toolsConfig;
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
                        section.DisplayOrder = displayOrder;
                        section.ScoreWeight = secDto.ScoreWeight > 0 ? secDto.ScoreWeight : 1.0m;
                        section.NavigationMode = navModeStr;
                        section.TimerPolicyJson = timerPolicy;
                        section.LayoutType = layoutStr;
                        section.OptionCount = secDto.OptionCount > 0 ? secDto.OptionCount : 4;
                        section.ClusterSize = secDto.ClusterSize > 0 ? secDto.ClusterSize : 1;
                        section.UnitCount = secDto.UnitCount > 0 ? secDto.UnitCount : 1;
                        section.HideOptionText = secDto.HideOptionText;
                        section.RequirePassage = requirePassage;
                        section.RequireAudio = requireAudio;
                        section.RequireImage = secDto.RequireImage;
                        section.RequiredKeywordCount = secDto.RequiredKeywordCount;
                        section.ExpectedQuestionCount = secDto.ExpectedQuestionCount;
                        section.AllowedComponentTypesJson = allowedTypes;
                        section.ToolsConfigJson = toolsConfig;
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
                            DisplayOrder = displayOrder,
                            ScoreWeight = secDto.ScoreWeight > 0 ? secDto.ScoreWeight : 1.0m,
                            NavigationMode = navModeStr,
                            TimerPolicyJson = timerPolicy,
                            LayoutType = layoutStr,
                            OptionCount = secDto.OptionCount > 0 ? secDto.OptionCount : 4,
                            ClusterSize = secDto.ClusterSize > 0 ? secDto.ClusterSize : 1,
                            UnitCount = secDto.UnitCount > 0 ? secDto.UnitCount : 1,
                            HideOptionText = secDto.HideOptionText,
                            RequirePassage = requirePassage,
                            RequireAudio = requireAudio,
                            RequireImage = secDto.RequireImage,
                            RequiredKeywordCount = secDto.RequiredKeywordCount,
                            ExpectedQuestionCount = secDto.ExpectedQuestionCount,
                            AllowedComponentTypesJson = allowedTypes,
                            ToolsConfigJson = toolsConfig
                        };
                        db.Sections.Add(section);
                    }
                }

                totalSections++;
            }
        }

        await db.SaveChangesAsync(cancellationToken);

        // E1.2 (#147): bands and rubric, written in the same save as the certificate that owns
        // them. Before this they lived only behind /admin/score-conversion, so a certificate
        // created here had zero bands and could never produce a BandCode.
        await PersistScalesAsync(cert.CertId, request, cancellationToken);
        await PersistBandsAsync(cert.CertId, request.Bands!, cancellationToken);
        await PersistCriteriaAsync(cert.CertId, request.Criteria, cancellationToken);

        return new SaveCertificateWizardResultDto(
            cert.CertId,
            cert.Code,
            cert.Name,
            totalSkills,
            totalSections);
    }

    /// <summary>
    /// The raw→scaled rows the engine reads. Generated per skill from that skill's own question
    /// count and range: a 10-question skill and a 40-question skill must not share one raw axis,
    /// or the short skill's top raw score maps to a fraction of its own ceiling.
    /// </summary>
    private async Task PersistScalesAsync(
        int certId, SaveCertificateWizardRequest request, CancellationToken cancellationToken)
    {
        var skills = await db.Skills
            .Where(s => s.CertId == certId)
            .ToListAsync(cancellationToken);

        // Question count per skill, from the sections the request declared.
        var rawPerSkill = request.Skills
            .Where(s => !string.IsNullOrWhiteSpace(s.Code))
            .Select(s => new
            {
                Code = s.Code.Trim().ToUpperInvariant(),
                Raw = (s.Sections ?? [])
                    .Sum(sec => sec.ExpectedQuestionCount),
            })
            .ToDictionary(x => x.Code, x => x.Raw, StringComparer.OrdinalIgnoreCase);

        var existing = await db.ScoreScales
            .Where(s => s.CertId == certId && s.ExamId == null && s.SkillId != null)
            .ToListAsync(cancellationToken);
        if (existing.Count > 0)
        {
            db.ScoreScales.RemoveRange(existing);
            await db.SaveChangesAsync(cancellationToken);
        }

        var min = request.MinScaledScore!.Value;
        var max = request.MaxScaledScore!.Value;
        var step = request.ScoreStep!.Value;

        foreach (var skill in skills)
        {
            // Each skill maps its own raw axis onto the certificate's scale.
            var maxRaw = rawPerSkill.TryGetValue(skill.Code, out var raw) && raw > 0
                ? raw
                : max - min;

            for (var rawScore = 0; rawScore <= maxRaw; rawScore++)
            {
                var fraction = maxRaw == 0 ? 0d : (double)rawScore / maxRaw;
                var scaled = min + fraction * (max - min);

                // Snap to the skill's grid. Every raw score maps, so the engine interpolates
                // between rows rather than falling back to a guessed range.
                var snapped = (int)Math.Round(scaled / step, MidpointRounding.AwayFromZero) * step;

                db.ScoreScales.Add(new ScoreScale
                {
                    CertId = certId,
                    SkillId = skill.SkillId,
                    ExamId = null,
                    RawScore = rawScore,
                    ScaledScore = Math.Clamp(snapped, min, max)
                });
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>
    /// Writes the certificate's bands. Bands are referenced by code from TargetBands,
    /// Recommendations and both ends of PathTemplates, so a band that is still referenced cannot be
    /// deleted — the four FKs would refuse it. Rows are updated in place by code where possible and
    /// only removed when nothing points at them.
    /// </summary>
    private async Task PersistBandsAsync(
        int certId, IReadOnlyList<SaveScoreBandWizardDto> bands, CancellationToken cancellationToken)
    {
        var existing = await db.ScoreBands
            .Where(b => b.CertId == certId)
            .ToListAsync(cancellationToken);

        await using var tx = await db.Database.BeginTransactionAsync(cancellationToken);

        var incoming = bands.OrderBy(b => b.MinTotal).ToList();
        var incomingCodes = incoming.Select(b => b.Code.Trim()).ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Bands that disappeared from the form. Deleting one still referenced by a learner's target
        // band would violate FK_TargetBands_Band and its siblings. #232 removed the fourth referrer
        // (PathTemplates), so the target band is the only surviving one.
        var removals = existing
            .Where(b => !incomingCodes.Contains(b.Code))
            .ToList();
        if (removals.Count > 0)
        {
            var referencedCodes = (await db.TargetBands
                    .Where(t => t.CertId == certId)
                    .Select(t => t.TargetBandCode)
                    .ToListAsync(cancellationToken))
                .Where(code => code is not null)
                .Select(code => code!)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var blocked = removals.Where(b => referencedCodes.Contains(b.Code)).ToList();
            if (blocked.Count > 0)
            {
                throw new ScoreConversionException(
                    ScoreConversionException.InvalidData,
                    "Cannot remove " + string.Join(", ", blocked.Select(b => $"'{b.Code}'")) +
                    " — a learner target band or learning-path template still uses it.");
            }

            db.ScoreBands.RemoveRange(removals);
            await db.SaveChangesAsync(cancellationToken);
        }

        // trg_ScoreBands_NoOverlap is AFTER INSERT/UPDATE and validates the table as a whole, so
        // shifting a boundary row by row trips it on the half-written pair. One set-based write
        // presents the trigger with the finished set.
        if (incoming.Count > 0)
        {
            var parameters = new List<object> { certId };
            var selects = new List<string>();
            for (var i = 0; i < incoming.Count; i++)
            {
                var b = incoming[i];
                var p = i * 5 + 1;
                selects.Add($"SELECT {{{p}}} AS Code, {{{p + 1}}} AS Name, {{{p + 2}}} AS MinTotal, {{{p + 3}}} AS MaxTotal, {{{p + 4}}} AS DisplayOrder");
                parameters.Add(b.Code.Trim());
                parameters.Add(b.Name.Trim());
                parameters.Add(b.MinTotal);
                parameters.Add(b.MaxTotal);
                parameters.Add(b.DisplayOrder);
            }

            // UPDATE first (rows that already exist, matched by code), then INSERT the genuinely new
            // ones, so no band is ever duplicated and the trigger sees one coherent statement each.
            var updateSql =
                "UPDATE b SET b.Name = v.Name, b.MinTotal = v.MinTotal, b.MaxTotal = v.MaxTotal, b.DisplayOrder = v.DisplayOrder " +
                "FROM ScoreBands b JOIN (" + string.Join(" UNION ALL ", selects) + ") v ON b.CertId = {0} AND b.Code = v.Code";
            await db.Database.ExecuteSqlRawAsync(updateSql, parameters.ToArray(), cancellationToken);

            var insertSql =
                "INSERT INTO ScoreBands (CertId, Code, Name, MinTotal, MaxTotal, DisplayOrder) " +
                "SELECT {0}, v.Code, v.Name, v.MinTotal, v.MaxTotal, v.DisplayOrder " +
                "FROM (" + string.Join(" UNION ALL ", selects) + ") v " +
                "WHERE NOT EXISTS (SELECT 1 FROM ScoreBands b WHERE b.CertId = {0} AND b.Code = v.Code)";
            await db.Database.ExecuteSqlRawAsync(insertSql, parameters.ToArray(), cancellationToken);
        }

        await tx.CommitAsync(cancellationToken);
    }


    private async Task PersistCriteriaAsync(
        int certId, IReadOnlyList<SaveGradingCriterionWizardDto>? criteria, CancellationToken cancellationToken)
    {
        if (criteria is null || criteria.Count == 0)
        {
            return;
        }

        var skills = await db.Skills
            .Where(s => s.CertId == certId)
            .ToListAsync(cancellationToken);
        var skillIdsByCode = skills.ToDictionary(s => s.Code, s => s.SkillId, StringComparer.OrdinalIgnoreCase);

        foreach (var dto in criteria)
        {
            if (!skillIdsByCode.TryGetValue(dto.SkillCode.Trim(), out var skillId))
            {
                throw new ArgumentException(
                    $"Criterion '{dto.Code}' refers to skill '{dto.SkillCode}', which this certificate does not define.",
                    nameof(criteria));
            }

            var existing = await db.GradingCriteria
                .FirstOrDefaultAsync(
                    c => c.CertId == certId
                         && c.SkillId == skillId
                         && (dto.CriterionId.HasValue
                             ? c.CriterionId == dto.CriterionId.Value
                             : c.Code == dto.Code),
                    cancellationToken);

            if (existing is null)
            {
                db.GradingCriteria.Add(new GradingCriterion
                {
                    CertId = certId,
                    SkillId = skillId,
                    Code = dto.Code.Trim(),
                    Name = dto.Name.Trim(),
                    MaxScore = dto.MaxScore,
                    Weight = dto.Weight > 0 ? dto.Weight : 1.0m,
                    DisplayOrder = dto.DisplayOrder,
                    RubricDescription = dto.RubricDescription?.Trim()
                });
            }
            else
            {
                existing.Code = dto.Code.Trim();
                existing.Name = dto.Name.Trim();
                existing.MaxScore = dto.MaxScore;
                existing.Weight = dto.Weight > 0 ? dto.Weight : 1.0m;
                existing.DisplayOrder = dto.DisplayOrder;
                existing.RubricDescription = dto.RubricDescription?.Trim();
            }
        }

        await db.SaveChangesAsync(cancellationToken);
    }

    private sealed record SectionModalityDefaults(
        NavigationModeDto NavigationMode,
        LayoutTypeDto LayoutType,
        bool RequirePassage,
        bool RequireAudio,
        string? AllowedComponentTypesJson,
        string? TimerPolicyJson,
        string? ToolsConfigJson);

    private static SectionModalityDefaults ModalityDefaults(string modality) => modality switch
    {
        "listening" => new(
            NavigationModeDto.AudioDriven,
            LayoutTypeDto.StandardVertical,
            RequirePassage: false,
            RequireAudio: true,
            AllowedComponentTypesJson: "[\"mcq\"]",
            TimerPolicyJson: "{\"timerMode\":\"AUDIO_PLUS_CHECK\",\"reviewSeconds\":120}",
            ToolsConfigJson: null),
        "reading" => new(
            NavigationModeDto.FullSandbox,
            LayoutTypeDto.SplitHorizontal,
            RequirePassage: true,
            RequireAudio: false,
            AllowedComponentTypesJson: "[\"mcq\"]",
            TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
            ToolsConfigJson: "{\"highlighter\":true,\"notes\":true}"),
        "writing" => new(
            NavigationModeDto.FullSandbox,
            LayoutTypeDto.SplitHorizontal,
            RequirePassage: false,
            RequireAudio: false,
            AllowedComponentTypesJson: "[\"free_response\"]",
            TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
            ToolsConfigJson: "{\"wordCount\":true,\"disableSpellcheck\":true}"),
        "speaking" => new(
            NavigationModeDto.SystemPacedAuto,
            LayoutTypeDto.StandardVertical,
            RequirePassage: false,
            RequireAudio: false,
            AllowedComponentTypesJson: "[\"free_response\"]",
            TimerPolicyJson: "{\"timerMode\":\"ITEM_FIXED\"}",
            ToolsConfigJson: null),
        _ => new(
            NavigationModeDto.FullSandbox,
            LayoutTypeDto.StandardVertical,
            RequirePassage: false,
            RequireAudio: false,
            AllowedComponentTypesJson: null,
            TimerPolicyJson: "{\"timerMode\":\"SECTION_SHARED\"}",
            ToolsConfigJson: null)
    };

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
