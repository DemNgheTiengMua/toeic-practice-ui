using System;
using System.Collections.Generic;

namespace BusinessObjects;

/// <summary>
/// #216 / PLC-14: what a learner sees when they come back after closing the tab.
/// <para>
/// <b>Why this is not the take view.</b> The take view answers "what item am I on"; this answers "what
/// state is my sitting in" - and PLC-14 names five facts: the start date, which skills are done, the
/// active skill and stage, the count of synced responses, and the two choices. Reading the take view
/// to answer that would have to render a question, which is the advancing behaviour a resume screen
/// must not do.
/// </para>
/// </summary>
public sealed record PlacementResumeView(
    int PlacementId,
    int AttemptId,
    string ExamName,
    string CertCode,
    DateTimeOffset StartedAt,
    DateTimeOffset ExpiresAt,
    IReadOnlyList<PlacementResumeSkill> Skills,
    int? ActiveSkillId,
    string? ActiveSkillName,
    /// <summary>1-based index of the active stage among the sitting's stages; null when unknown.</summary>
    int? ActiveStage,
    int TotalStages,
    /// <summary>
    /// The count of answers the SERVER holds - <c>AttemptAnswers</c> rows that record a choice, plus
    /// free responses that carry text or audio. Never the client's queued-but-unsent payloads: those
    /// live in the browser and the server cannot see them, so counting them here would be a number the
    /// screen prints and the database does not have.
    /// </summary>
    int SyncedAnswerCount,
    int TotalQuestions,
    /// <summary>The exact item the learner was on. Null only for a paper with no questions.</summary>
    int? LastQuestionId);

/// <summary>One skill's position in a resumed sitting: done, in progress, or not reached.</summary>
public sealed record PlacementResumeSkill(
    int SkillId,
    string Code,
    string Name,
    bool IsDone,
    bool IsActive,
    int AnsweredCount,
    int TotalQuestions);
