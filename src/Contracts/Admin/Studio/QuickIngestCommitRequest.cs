using System;
using System.Collections.Generic;

namespace Contracts;

public sealed record QuickIngestCommitRequest(
    int SectionId,
    string Mode, // "append" | "replace"
    IReadOnlyList<QuickIngestParsedQuestionDto> Questions,
    // #189: the level and topic the pasted text belongs to. A parser cannot know them from the text,
    // so the screen asks once per batch rather than duct-taping a level onto every line.
    string? CefrLevel = null,
    string? TopicCode = null,
    string TargetBands = "ALL");
