using System.Text.Json.Serialization;

namespace Contracts;

/// <summary>
/// #232 / D10: the three execution paths, as wire values. Each names a different surface, so the client
/// routes on the step type rather than guessing from a nullable reference.
/// </summary>
[JsonConverter(typeof(JsonStringEnumConverter<PathStepTypeDto>))]
public enum PathStepTypeDto
{
    [JsonStringEnumMemberName("diagnostic_drill")] DiagnosticDrill,
    [JsonStringEnumMemberName("focus_practice")] FocusPractice,
    [JsonStringEnumMemberName("review_spaced")] ReviewSpaced,
    [JsonStringEnumMemberName("concept_lesson")] ConceptLesson,
    [JsonStringEnumMemberName("expanded_remedial")] ExpandedRemedial,
    [JsonStringEnumMemberName("mock_section")] MockSection,
}

/// <summary>D10's three sizes, as wire values.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<SizingTierDto>))]
public enum SizingTierDto
{
    [JsonStringEnumMemberName("mastered")] Mastered,
    [JsonStringEnumMemberName("developing")] Developing,
    [JsonStringEnumMemberName("low")] Low,
}

/// <summary>#232: where a step opens, so the client does not infer it from the step type.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<StepRouteDto>))]
public enum StepRouteDto
{
    [JsonStringEnumMemberName("item_selector")] ItemSelector,
    [JsonStringEnumMemberName("lesson_reader")] LessonReader,
    [JsonStringEnumMemberName("attempt_engine")] AttemptEngine,
}
