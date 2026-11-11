using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;

namespace Services;

/// <summary>
/// #271: one <c>(topic, level)</c> cell of the learner's history, as <c>vw_UserTopicAccuracy</c>
/// reported it.
/// <para>
/// <b>It replaces <c>InsightCategoryAccuracy</c>, and it is a cell rather than a topic.</b> The
/// percentage and the counts arrive computed by the view; a builder that divided
/// <see cref="Correct"/> by <see cref="Answered"/> would be a second definition of one figure (D3).
/// </para>
/// <para>
/// <b><see cref="LevelCode"/> is part of the identity.</b> "R-DET" at B1 and "R-DET" at B2 are two
/// different claims about the learner, so the narrator and the lucky-guess rule match a question to a
/// cell by both fields - matching on the topic alone would attribute one level's history to another.
/// </para>
/// </summary>
public record InsightTopicAccuracy(
    int TopicId,
    string TopicCode,
    string TopicName,
    string LevelCode,
    int Answered,
    int Correct,
    int AccuracyPercentage
);
