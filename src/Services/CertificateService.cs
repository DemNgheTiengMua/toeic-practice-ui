using BusinessObjects;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface ICertificateService
{
    Task<IReadOnlyList<CertificateCatalog>> GetCatalogAsync(CancellationToken cancellationToken = default);
}

public sealed class CertificateService(CompanionDbContext db) : ICertificateService
{
    public async Task<IReadOnlyList<CertificateCatalog>> GetCatalogAsync(CancellationToken cancellationToken = default)
    {
        // Loaded as three flat queries rather than filtered includes: the scaffold names two
        // section navigations per skill (SectionSkills / SectionSkillNavigations), and the
        // composite FKs make filtered includes fragile to re-scaffold. Three round-trips are
        // cheap at this data size and read plainly.
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
                                sec.ExpectedQuestionCount)).ToList())).ToList(),
                bands.Where(b => b.CertId == c.CertId)
                    .Select(b => new BandCatalog(
                        b.Code,
                        b.Name,
                        b.MinTotal,
                        b.MaxTotal)).ToList()))
            .ToList();
    }
}
