using System.Threading;
using System.Threading.Tasks;
using Contracts;

namespace Services;

public interface IExamGeneratorService
{
    /// <summary>
    /// Generates an exam package conforming to sample-exam-toeic-2skill.json via Claude Sonnet 3.7
    /// from the specified blueprint, validates schema and certificate isolation, and inserts it
    /// into Exams, Sections, QuestionGroups, Questions, and QuestionOptions.
    /// </summary>
    Task<ExamGenerationResultDto> GenerateAndIngestExamAsync(
        ExamBlueprintDto blueprint,
        int adminUserId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Ingests a pre-authored manifest JSON directly with status override.
    /// </summary>
    Task<ExamGenerationResultDto> IngestManifestAsync(
        int certId,
        string manifestJson,
        int adminUserId,
        string? targetStatus = null,
        CancellationToken cancellationToken = default);
}
