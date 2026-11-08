using System.Text.Json;
using Contracts;

namespace WebMVC.Models;

public sealed record ExamTakePresentation
{
    public required int AttemptId { get; init; }
    public required string ExamName { get; init; }
    public string? AuthorizationCode { get; init; }
    public bool HasAuthorizationCode { get; init; }
    public required int TotalQuestions { get; init; }
    public required int AnsweredCount { get; init; }
    public required int FlaggedCount { get; init; }
    public required int UnansweredCount { get; init; }
    public required int ProgressPercent { get; init; }
    public required bool AllAnswered { get; init; }
    public required ExamTakeQuestionPresentation Question { get; init; }
    public required IReadOnlyList<ExamTakeQuestionPresentation> ClusterQuestions { get; init; }
    public required IReadOnlyList<ExamTakeNavigatorPresentation> Navigator { get; init; }
    public required IReadOnlyList<ExamTakePhasePresentation> Phases { get; init; }
    public AudioEquipmentCheckModel? AudioEquipment { get; init; }
    public ExamAudioPlayerModel? InitialAudio { get; init; }
    public ExamAudioPlayerModel AudioTemplate { get; } = new(AudioPath: string.Empty, Mode: "exam", TemplateOnly: true);
    public required string QuestionsJson { get; init; }
    public required string PhasesJson { get; init; }
    public required string SittingJson { get; init; }

    internal static ExamTakePresentation Create(AttemptTakeDto take, bool previewMode = false)
    {
        var q = take.CurrentQuestion;
        var questions = take.Questions ?? [q];
        var phases = take.Phases ?? [];
        var hasListening = questions.Any(item => item.Modality == ModalityDto.Listening);
        var hasSpeaking = questions.Any(item => item.Modality == ModalityDto.Speaking);
        var clusterQuestions = q.ClusterSize == 3
            ? questions.Where(item => (q.GroupId.HasValue && item.GroupId == q.GroupId)
                || (item.SectionId == q.SectionId && (item.DisplayOrder - 1) / 3 == (q.DisplayOrder - 1) / 3)).ToList()
            : [];
        if (q.ClusterSize == 3 && clusterQuestions.Count == 0) clusterQuestions.Add(q);

        // The default encoder escapes HTML-sensitive characters, including </script> in learner/content data.
        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        return new ExamTakePresentation
        {
            AttemptId = take.AttemptId,
            ExamName = take.ExamName,
            AuthorizationCode = take.AuthorizationCode,
            HasAuthorizationCode = !string.IsNullOrEmpty(take.AuthorizationCode),
            TotalQuestions = take.TotalQuestions,
            AnsweredCount = take.AnsweredCount,
            FlaggedCount = take.FlaggedCount,
            UnansweredCount = take.TotalQuestions - take.AnsweredCount,
            ProgressPercent = (int)Math.Round((double)take.AnsweredCount / Math.Max(1, take.TotalQuestions) * 100),
            AllAnswered = take.AnsweredCount >= take.TotalQuestions,
            Question = ExamTakeQuestionPresentation.Create(q, take.TotalQuestions),
            ClusterQuestions = clusterQuestions.Select(item => ExamTakeQuestionPresentation.Create(item, take.TotalQuestions, clustered: true)).ToArray(),
            Navigator = take.Navigator.Select(ExamTakeNavigatorPresentation.Create).ToArray(),
            Phases = phases.Select(phase => new ExamTakePhasePresentation(phase.SkillCode, phase.SkillName,
                q.SkillCode == phase.SkillCode ? "step" : "false")).ToArray(),
            AudioEquipment = hasListening || hasSpeaking
                ? new AudioEquipmentCheckModel(ShowInput: hasSpeaking, ShowOutput: hasListening, Sitting: true)
                : null,
            // #161: the player is titled with the section the question belongs to, not the literal
            // "Listening Audio" - audio is not the exclusive property of a listening section.
            InitialAudio = !string.IsNullOrEmpty(q.AudioPath) ? new ExamAudioPlayerModel(
                AudioPath: q.AudioPath, AllowReplay: false, SectionTitle: $"{q.SkillName} audio", BadgeText: "READY",
                StartMs: q.AudioStartMs, EndMs: q.AudioEndMs, PlayerId: "main-exam-audio", Mode: "exam") : null,
            QuestionsJson = JsonSerializer.Serialize(questions, jsonOptions),
            PhasesJson = JsonSerializer.Serialize(phases, jsonOptions),
            SittingJson = JsonSerializer.Serialize(new
            {
                take.AttemptId,
                take.ExamName,
                ExamId = take.ExamId,
                take.TotalQuestions,
                q.DisplayOrder,
                take.ActiveSkillId,
                take.ActiveUnitNumber,
                take.ActiveGroupId,
                PhaseDeadlineAt = take.PhaseDeadlineAt?.ToUnixTimeMilliseconds(),
                ExpiresAt = take.ExpiresAt.ToUnixTimeMilliseconds(),
                AttemptStartedAt = take.StartedAt.ToUnixTimeMilliseconds(),
                take.LastAudioOffsetMs,
                CandidateNotesJson = take.CandidateNotesJson ?? "",
                // #158: the shell reads this to stay read-only and to unlock navigation.
                PreviewMode = previewMode,
                CurrentQuestion = q
            }, jsonOptions)
        };
    }
}
