using System.Data.SqlClient;
using DataAccess;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Services.Tests;

/// <summary>
/// #269: a paper built on the pool, for test fixtures.
/// <para>
/// <b>Why this exists.</b> Sixty-nine test files seed a paper by inserting <c>QuestionGroups</c>,
/// <c>Questions</c> and <c>QuestionOptions</c> and then reading the ids back. Those three tables are
/// being dropped, so every one of those fixtures has to author the pool's shape instead: a stimulus for
/// the shared passage, an item per question, a version carrying the scored content, and a binding per
/// position.
/// </para>
/// <para>
/// <b>What it is not.</b> It is not a second content model - it writes exactly the rows
/// <c>IAdminItemService.CreateAsync</c> plus <c>BindItemAsync</c> would write, in one statement, because a
/// fixture that needs six ids does not want six round trips. A test that wants to prove the service does
/// its job still calls the service.
/// </para>
/// </summary>
public static class PoolFixture
{
    /// <summary>One authored position, with the ids a test asserts against.</summary>
    public sealed record Position(int ExamItemId, int ItemId, int ItemVersionId);

    /// <summary>
    /// A four-option MCQ at (<paramref name="sectionId"/>, position <paramref name="displayOrder"/>).
    /// <c>keyLabel</c> is 'A' by default; <c>passage</c> shares one stimulus across the positions that pass
    /// the same value, which is what makes a bundle.
    /// </summary>
    public static async Task<Position> AddMcqAsync(
        CompanionDbContext db,
        int examId,
        int certId,
        int sectionId,
        string modality,
        string stem,
        int displayOrder,
        string keyLabel = "A",
        string level = "B1",
        string? passage = null,
        string? explanation = null,
        int? stimulusId = null,
        int unitNumber = 1,
        string? hint = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var topicId = await FirstTopicIdAsync(db, modality, cancellationToken);

        int? resolvedStimulus = stimulusId;
        if (resolvedStimulus is null && !string.IsNullOrWhiteSpace(passage))
        {
            var stimulus = new BusinessObjects.Entities.Stimulus
            {
                CefrLevel = BusinessObjects.CefrLevel.B1,
                Passage = passage,
                DomainContext = "general",
                DisplayOrder = displayOrder,
            };
            db.Stimuli.Add(stimulus);
            await db.SaveChangesAsync(cancellationToken);
            resolvedStimulus = stimulus.StimulusId;
        }

        var item = new BusinessObjects.Entities.Item
        {
            Modality = modality,
            TopicId = topicId,
            CefrLevel = BusinessObjects.CefrLevel.TryParse(level, out var parsed) ? parsed : BusinessObjects.CefrLevel.B1,
            ItemType = ItemTypeFor(modality),
            RenderFormat = "mcq",
            ContentTier = "demo",
            LifecycleStatus = "active",
            StimulusId = resolvedStimulus,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        var version = new BusinessObjects.Entities.ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = stem,
            OptionA = "Option A",
            OptionB = "Option B",
            OptionC = "Option C",
            OptionD = "Option D",
            KeyLabel = keyLabel,
            Explanation = explanation ?? $"The key is {keyLabel}.",
            Hint = hint,
            RationaleA = "Rationale A.",
            RationaleB = "Rationale B.",
            RationaleC = "Rationale C.",
            RationaleD = "Rationale D.",
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);

        var binding = new BusinessObjects.Entities.ExamItem
        {
            ExamId = examId,
            CertId = certId,
            SectionId = sectionId,
            ItemId = item.ItemId,
            ItemVersionId = version.ItemVersionId,
            Modality = modality,
            DisplayOrder = displayOrder,
            UnitNumber = unitNumber,
        };
        db.ExamItems.Add(binding);
        await db.SaveChangesAsync(cancellationToken);

        // The option ids are the projection's, not a second formula: a test that answers has to echo what
        // the sitting renders, and deriving them here would let the two schemes drift.
        return new Position(binding.ExamItemId, item.ItemId, version.ItemVersionId);
    }

    /// <summary>
    /// A productive task at a position: no options, no key, and the word window and timers the task
    /// declares. This is the shape <c>CK_ItemVersions_OptionsComplete</c> calls the "no options" branch.
    /// </summary>
    public static async Task<Position> AddProductiveAsync(
        CompanionDbContext db,
        int examId,
        int certId,
        int sectionId,
        string modality,
        string stem,
        int displayOrder,
        int? minWords = null,
        int? maxWords = null,
        int? prepSeconds = null,
        int? responseSeconds = null,
        string? hint = null,
        string? referenceText = null,
        string? referenceKind = null,
        string level = "B1",
        int unitNumber = 1,
        /// <summary>
        /// A HYBRID_GROUP_LOCKED unit's own clock. It belongs to the STIMULUS, because a locked unit is one
        /// timed block that its positions share - not a property of any single task.
        /// </summary>
        int? stimulusResponseSeconds = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(db);

        var topicId = await FirstTopicIdAsync(db, modality, cancellationToken);

        // A locked unit's clock lives on the stimulus its positions share.
        BusinessObjects.Entities.Stimulus? stimulus = null;
        if (stimulusResponseSeconds.HasValue)
        {
            stimulus = new BusinessObjects.Entities.Stimulus
            {
                CefrLevel = BusinessObjects.CefrLevel.TryParse(level, out var lvl) ? lvl : BusinessObjects.CefrLevel.B1,
                DomainContext = "general",
                ResponseSeconds = stimulusResponseSeconds,
                DisplayOrder = displayOrder,
            };
            db.Stimuli.Add(stimulus);
            await db.SaveChangesAsync(cancellationToken);
        }

        var item = new BusinessObjects.Entities.Item
        {
            Modality = modality,
            TopicId = topicId,
            CefrLevel = BusinessObjects.CefrLevel.TryParse(level, out var parsed) ? parsed : BusinessObjects.CefrLevel.B1,
            ItemType = ItemTypeFor(modality),
            RenderFormat = "free_response",
            ContentTier = "demo",
            LifecycleStatus = "active",
            StimulusId = stimulus?.StimulusId,
        };
        db.Items.Add(item);
        await db.SaveChangesAsync(cancellationToken);

        var version = new BusinessObjects.Entities.ItemVersion
        {
            ItemId = item.ItemId,
            VersionNumber = 1,
            Stem = stem,
            MinWords = minWords,
            MaxWords = maxWords,
            PrepSeconds = prepSeconds,
            ResponseSeconds = responseSeconds,
            Hint = hint,
            ReferenceText = referenceText,
            ReferenceKind = referenceKind,
        };
        db.ItemVersions.Add(version);
        await db.SaveChangesAsync(cancellationToken);

        var binding = new BusinessObjects.Entities.ExamItem
        {
            ExamId = examId,
            CertId = certId,
            SectionId = sectionId,
            ItemId = item.ItemId,
            ItemVersionId = version.ItemVersionId,
            Modality = modality,
            DisplayOrder = displayOrder,
            UnitNumber = unitNumber,
        };
        db.ExamItems.Add(binding);
        await db.SaveChangesAsync(cancellationToken);

        return new Position(binding.ExamItemId, item.ItemId, version.ItemVersionId);
    }

    /// <summary>
    /// The paper's positions in sitting order, which is what a test asserting "question 1" means.
    /// </summary>
    public static Task<List<Position>> PositionsAsync(
        CompanionDbContext db, int examId, CancellationToken cancellationToken = default) =>
        db.ExamItems.AsNoTracking()
            .Where(i => i.ExamId == examId)
            .OrderBy(i => i.Section.Skill.DisplayOrder)
            .ThenBy(i => i.Section.DisplayOrder)
            .ThenBy(i => i.DisplayOrder)
            .Select(i => new Position(i.ExamItemId, i.ItemId, i.ItemVersionId))
            .ToListAsync(cancellationToken);

    /// <summary>
    /// The option id the sitting renders for one label of a position. Read from the projection rather than
    /// recomputed: a test that answers has to echo what the learner was shown.
    /// </summary>
    public static async Task<int> OptionIdAsync(
        CompanionDbContext db, int examId, int examItemId, string label,
        CancellationToken cancellationToken = default)
    {
        var attempt = await new Services.AttemptEngine(db).GetExamPreviewTakeAsync(examId, null, cancellationToken);
        var position = attempt.Questions!.Single(q => q.QuestionId == examItemId);
        return position.Options.Single(o => o.Label == label).OptionId;
    }

    /// <summary>The pool's semantic type for a modality, which <c>CK_Items_Modality_Type</c> pins.</summary>
    private static string ItemTypeFor(string modality) => modality switch
    {
        "listening" => "short_exchange",
        "reading" => "sentence_cloze",
        "speaking" => "descriptive_monologue",
        "writing" => "transactional_correspondence",
        _ => "sentence_cloze",
    };

    /// <summary>
    /// A topic of this modality. The vocabulary is a closed set of twenty that no certificate owns, so a
    /// fixture needs one to exist; a database without it cannot hold pool stock at all. A fresh test
    /// database starts empty, so the fixture authors the row rather than making every caller remember to.
    /// </summary>
    private static async Task<int> FirstTopicIdAsync(
        CompanionDbContext db, string modality, CancellationToken cancellationToken)
    {
        var topicId = await db.Topics.AsNoTracking()
            .Where(t => t.Modality == modality)
            .OrderBy(t => t.TopicId)
            .Select(t => (int?)t.TopicId)
            .FirstOrDefaultAsync(cancellationToken);

        if (topicId is not null)
        {
            return topicId.Value;
        }

        var topic = new BusinessObjects.Entities.Topic
        {
            Modality = modality,
            Code = CodeFor(modality),
            Name = NameFor(modality),
        };
        db.Topics.Add(topic);
        await db.SaveChangesAsync(cancellationToken);
        return topic.TopicId;
    }

    /// <summary>The closed vocabulary's code for a modality, so a fixture row is not an invented one.</summary>
    private static string CodeFor(string modality) => modality switch
    {
        "listening" => "L-DET",
        "reading" => "R-DET",
        "speaking" => "S-DESC",
        "writing" => "W-CORR",
        _ => "R-DET",
    };

    private static string NameFor(string modality) => modality switch
    {
        "listening" => "Listening for detail",
        "reading" => "Reading for detail",
        "speaking" => "Describing",
        "writing" => "Transactional correspondence",
        _ => "Reading for detail",
    };
}
