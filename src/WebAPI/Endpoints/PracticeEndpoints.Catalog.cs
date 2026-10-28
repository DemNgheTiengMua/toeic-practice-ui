using System.ComponentModel.DataAnnotations;
using BusinessObjects;
using Contracts;
using Services;
using static WebAPI.Endpoints.EndpointHelpers;

namespace WebAPI.Endpoints;

public static partial class PracticeEndpoints
{
    private static void MapCatalog(RouteGroupBuilder practice)
    {

        practice.MapGet("/catalog", async (HttpContext http, IPracticeService practiceService, CancellationToken cancellationToken) =>
        {
            var view = await practiceService.GetPracticeSectionsAsync(CurrentUserId(http), cancellationToken);
            static PracticeSectionDto ToSectionDto(PracticeSectionItem s) => new(
                s.SectionId, s.SkillCode, s.SkillName, s.Modality.MapTo<ModalityDto>(), s.Code, s.Name, s.OptionCount,
                s.ExpectedQuestionCount, s.AvailableQuestionCount, s.AccuracyPercentage, s.AnsweredCount, s.DisplayOrder);

            return Results.Ok(new PracticeCatalogDto(
                ToCertificateDto(view.Certificate),
                view.ListeningSections.Select(ToSectionDto).ToList(),
                view.ReadingSections.Select(ToSectionDto).ToList(),
                view.OtherSections.Select(ToSectionDto).ToList(),
                view.WeakestSection is { } w ? ToSectionDto(w) : null,
                view.UnfinishedSession is { } ufs
                    ? new PracticeSessionDto(
                        ufs.SessionId, ufs.SectionId, ufs.SectionCode, ufs.SectionName, ufs.Modality.MapTo<ModalityDto>(),
                        ufs.QuestionCount, ufs.Status.MapTo<PracticeStatusDto>(), ufs.CorrectCount, ufs.QuestionSource.MapTo<QuestionSourceDto>(), ufs.FeedbackMode.MapTo<FeedbackModeDto>(),
                        ufs.StartedAt, ufs.FinishedAt)
                    : null));
        });
    }
}
