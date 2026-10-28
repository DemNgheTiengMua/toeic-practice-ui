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
}
