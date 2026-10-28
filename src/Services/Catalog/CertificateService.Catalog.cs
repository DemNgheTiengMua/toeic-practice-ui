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
}
