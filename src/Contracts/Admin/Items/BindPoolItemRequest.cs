using System;

namespace Contracts;

/// <summary>
/// #188: the second, explicit act. An authored item carries no exam reference; this is the request
/// that puts one of its versions on a paper, at a position the paper's section declares.
/// </summary>
public sealed record BindPoolItemRequest(
    int SectionId,
    int ItemVersionId,
    int DisplayOrder,
    int UnitNumber = 1);

/// <summary>The binding as it was written: which version now occupies which position.</summary>
public sealed record ExamItemBindingDto(
    int ExamItemId,
    int ExamId,
    int SectionId,
    int CertId,
    int ItemId,
    int ItemVersionId,
    ModalityDto Modality,
    int DisplayOrder,
    int UnitNumber);
