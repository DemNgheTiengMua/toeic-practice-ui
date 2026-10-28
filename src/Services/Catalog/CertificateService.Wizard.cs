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
