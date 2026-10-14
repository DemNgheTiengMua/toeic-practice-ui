using Contracts;
using Services;

namespace WebAPI.Endpoints;

public static class CatalogEndpoints
{
    public static void MapCatalogEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/api/certificates", async (ICertificateService certificates, CancellationToken cancellationToken) =>
        {
            var catalog = await certificates.GetCatalogAsync(cancellationToken);
            var dtos = catalog.Select(c => new CertificateDto(
                c.Code,
                c.Name,
                c.Description,
                c.Skills.Select(s => new SkillDto(
                    s.Code,
                    s.Name,
                    s.Modality,
                    s.DurationMinutes,
                    s.Sections.Select(sec => new SectionDto(sec.Code, sec.Name, sec.ExpectedQuestionCount)).ToList())).ToList(),
                c.Bands.Select(b => new BandDto(b.Code, b.Name, b.MinTotal, b.MaxTotal)).ToList()));
            return Results.Ok(dtos);
        });

        app.MapGet("/api/packages", async (IPackageService packages, CancellationToken cancellationToken) =>
        {
            var list = await packages.GetActivePackagesAsync(cancellationToken);
            return Results.Ok(list);
        });
    }
}
