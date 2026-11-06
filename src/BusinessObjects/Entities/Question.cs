using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace BusinessObjects.Entities;

[Index("GroupId", "DisplayOrder", Name = "IX_Questions_Group")]
[Index("SectionId", Name = "IX_Questions_Section")]
[Index("QuestionId", "ExamId", Name = "UQ_Questions_Id_Exam", IsUnique = true)]
[Index("QuestionId", "SectionId", Name = "UQ_Questions_Id_Section", IsUnique = true)]
[Index("QuestionId", "SkillId", Name = "UQ_Questions_Id_Skill", IsUnique = true)]
[Index("QuestionId", "QuestionType", Name = "UQ_Questions_Id_Type", IsUnique = true)]
public partial class Question
{
    [Key]
    public int QuestionId { get; set; }

    public int GroupId { get; set; }

    public int ExamId { get; set; }

    public int SectionId { get; set; }

    public int SkillId { get; set; }

    public string Stem { get; set; } = null!;

    /// <summary>
    /// #229 / D1: dormant. Every authoring and import path writes <see cref="CefrLevel"/> instead, so a
    /// row written today carries NULL here; #231 drops the column and this member. It stays nullable
    /// because the alternative is a path inventing a 3 to satisfy NOT NULL, which is the silent
    /// default #229 exists to remove. Never read to produce a level: there is no 1-5 to A1-C2 mapping.
    /// </summary>
    public int? DifficultyLevel { get; set; }

    /// <summary>
    /// #229 / D1: the level the item is authored at, mapped to <c>Questions.CefrLevel</c> by its code.
    /// Authored, so the reportable floor (<c>pre_a1</c>) is refused on the way in and on the way out.
    /// </summary>
    public CefrLevel? CefrLevel { get; set; }

    [StringLength(30)]
    [Unicode(false)]
    public string QuestionType { get; set; } = null!;

    public string? Explanation { get; set; }

    public int? CategoryId { get; set; }

    public int? PrepSeconds { get; set; }

    public int? ResponseSeconds { get; set; }

    public int? MinWords { get; set; }

    public int? MaxWords { get; set; }

    public int? MaxSelections { get; set; }

    public bool AllowNumber { get; set; } = true;

    public string? AcceptedAnswersJson { get; set; }

    public string? RequiredKeywords { get; set; }

    [StringLength(1000)]
    public string? Hint { get; set; }

    /// <summary>
    /// #152: what a spoken answer is judged against - the words to read for a Read Aloud question,
    /// the description of the picture for a Describe a Picture one. NULL means no reference exists,
    /// and the question is graded as unscripted with no relevance score. Never backfilled.
    /// </summary>
    [StringLength(1000)]
    public string? SampleAnswerOrImageDesc { get; set; }

    /// <summary>
    /// #152: what <see cref="SampleAnswerOrImageDesc"/> is (CK_Questions_ReferenceKind). Null with a
    /// text is not a reference this system knows how to use. It is a question-level fact on purpose:
    /// the group's picture cannot say what a question's own text means.
    /// </summary>
    [StringLength(12)]
    [Unicode(false)]
    public string? ReferenceKind { get; set; }

    public int DisplayOrder { get; set; }

    public DateTimeOffset? UpdatedAt { get; set; }

    [InverseProperty("QuestionNavigation")]
    public virtual ICollection<AttemptAnswer> AttemptAnswerQuestionNavigations { get; set; } = new List<AttemptAnswer>();

    [InverseProperty("Question")]
    public virtual ICollection<AttemptAnswer> AttemptAnswerQuestions { get; set; } = new List<AttemptAnswer>();

    [ForeignKey("CategoryId")]
    [InverseProperty("Questions")]
    public virtual QuestionCategory? Category { get; set; }

    [InverseProperty("Question")]
    public virtual ICollection<ExamAttempt> ExamAttempts { get; set; } = new List<ExamAttempt>();

    [InverseProperty("Question1")]
    public virtual ICollection<FreeResponse> FreeResponseQuestion1s { get; set; } = new List<FreeResponse>();

    [InverseProperty("Question2")]
    public virtual ICollection<FreeResponse> FreeResponseQuestion2s { get; set; } = new List<FreeResponse>();

    [InverseProperty("QuestionNavigation")]
    public virtual ICollection<FreeResponse> FreeResponseQuestionNavigations { get; set; } = new List<FreeResponse>();

    [InverseProperty("Question")]
    public virtual ICollection<FreeResponse> FreeResponseQuestions { get; set; } = new List<FreeResponse>();

    [ForeignKey("GroupId")]
    [InverseProperty("QuestionGroups")]
    public virtual QuestionGroup Group { get; set; } = null!;

    [InverseProperty("Question")]
    public virtual ICollection<PracticeAnswer> PracticeAnswerQuestions { get; set; } = new List<PracticeAnswer>();

    [ForeignKey("GroupId, ExamId")]
    [InverseProperty("QuestionQuestionGroups")]
    public virtual QuestionGroup QuestionGroup { get; set; } = null!;

    [ForeignKey("GroupId, SectionId")]
    [InverseProperty("QuestionQuestionGroupNavigations")]
    public virtual QuestionGroup QuestionGroupNavigation { get; set; } = null!;

    [InverseProperty("Question")]
    public virtual QuestionOption? QuestionOptionQuestion { get; set; }

    [InverseProperty("QuestionNavigation")]
    public virtual ICollection<QuestionOption> QuestionOptionQuestionNavigations { get; set; } = new List<QuestionOption>();

    [InverseProperty("Question")]
    public virtual ICollection<SavedQuestion> SavedQuestions { get; set; } = new List<SavedQuestion>();

    [ForeignKey("SectionId, SkillId")]
    [InverseProperty("Questions")]
    public virtual Section Section { get; set; } = null!;

    /// <summary>#155: learners who have met this question, with their per-question cooldown.</summary>
    [InverseProperty("Question")]
    public virtual ICollection<UserQuestionHistory> UserQuestionHistories { get; set; } = new List<UserQuestionHistory>();
}
