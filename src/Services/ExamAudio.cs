using Contracts;

namespace Services;

/// <summary>The audio a learner hears for one question group: a file, optionally limited to a slice.</summary>
public sealed record ResolvedGroupAudio(string Path, int? StartMs, int? EndMs);

/// <summary>
/// Decides what a question group plays (CONTEXT.md "Audio slicing"). A section's master track uploaded
/// in Exam Studio (ExamSectionAudio) is the source of truth: when it exists every group plays it, cut
/// to the group's slice when one is set. A group's own AudioPath is the fallback for sections without
/// a master track (seeded and imported exams). Exam sitting, review and the publish audit all use
/// this, so what the audit passes is what the learner hears.
/// </summary>
public static class ExamAudio
{
    public static ResolvedGroupAudio? Resolve(string? groupAudioPath, int? startMs, int? endMs, string? sectionMasterPath)
    {
        var path = !string.IsNullOrWhiteSpace(sectionMasterPath) ? sectionMasterPath
            : !string.IsNullOrWhiteSpace(groupAudioPath) ? groupAudioPath
            : null;
        if (path is null)
        {
            return null;
        }

        var hasSlice = startMs is >= 0 && endMs.HasValue && endMs.Value > startMs.Value;
        return new ResolvedGroupAudio(
            MediaUrlHelper.NormalizeMediaUrl(path)!,
            hasSlice ? startMs : null,
            hasSlice ? endMs : null);
    }
}
