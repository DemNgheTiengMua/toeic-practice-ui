namespace Contracts;

/// <summary>Wire DTOs for the certificates endpoint. JSON-serialized by the API, deserialized by the MVC site.</summary>
public sealed record CertificateDto(
    string Code,
    string Name,
    string? Description,
    IReadOnlyList<SkillDto> Skills,
    IReadOnlyList<BandDto> Bands);

public sealed record SkillDto(
    string Code,
    string Name,
    ModalityDto Modality,
    int? DurationMinutes,
    IReadOnlyList<SectionDto> Sections);

public sealed record SectionDto(
    string Code,
    string Name,
    int? ExpectedQuestionCount);

public sealed record BandDto(
    string Code,
    string Name,
    int MinTotal,
    int MaxTotal);
