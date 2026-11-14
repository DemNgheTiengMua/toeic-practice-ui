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
                s.ExpectedQuestionCount, s.AvailableQuestionCount, s.AccuracyPercentage, s.AnsweredCount, s.DisplayOrder,
                s.TotalCategoryCount, s.CategoriesInCooldown, s.NextReviewDueAt, s.IsUnlocked);

            return Results.Ok(new PracticeCatalogDto(
                ToCertificateDto(view.Certificate),
                view.ListeningSections.Select(ToSectionDto).ToList(),
                view.ReadingSections.Select(ToSectionDto).ToList(),
                view.OtherSections.Select(ToSectionDto).ToList(),
                view.WeakestSection is { } w ? ToSectionDto(w) : null,
                view.UnfinishedSession is { } ufs
                    ? new PracticeSessionDto(
                        ufs.SessionId, ufs.SectionId, ufs.SectionCode, ufs.SectionName, ufs.Modality.MapTo<ModalityDto>(),
                        ufs.QuestionCount, ufs.Status.MapTo<PracticeStatusDto>(), ufs.CorrectCount, ufs.QuestionSource.MapTo<QuestionSourceDto>(),
                        ufs.StartedAt, ufs.FinishedAt, ufs.TopicId, ufs.CefrLevel?.Code)
                    : null,
                (view.Speaking ?? []).Select(ToSectionDto).ToList(),
                view.SpeakingPracticeExamId));
        });
    }

    /// <summary>
    /// #238: mark the cells the plan's own top-three point at. Compared by <c>(topic, level)</c> only —
    /// a plan coordinate is a topic at a level, and two skills pointing at one cell still highlight that
    /// one cell. <b>Not</b> the first three rows: the priority is the plan's set, so a cell that sorts
    /// fifth is still highlighted and a cell that sorts first is not.
    /// </summary>
    public static IReadOnlyList<PracticeCellDto> MarkPrioritised(
        IReadOnlyList<PracticeCellDto> cells, IReadOnlyList<PrioritisedCoordinate> priorities)
    {
        var priorityCells = priorities
            .Select(p => (p.TopicId, p.Level.Code))
            .ToHashSet();

        return cells
            .Select(c => c with { IsPrioritised = priorityCells.Contains((c.TopicId, c.LevelCode)) })
            .ToList();
    }
}
