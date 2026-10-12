using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace DataAccess.Scaffold;

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

    public int DifficultyLevel { get; set; }

    [StringLength(14)]
    [Unicode(false)]
    public string QuestionType { get; set; } = null!;

    public string? Explanation { get; set; }

    public int? CategoryId { get; set; }

    public int? PrepSeconds { get; set; }

    public int? ResponseSeconds { get; set; }

    public int? MinWords { get; set; }

    public int? MaxWords { get; set; }

    [StringLength(1000)]
    public string? Hint { get; set; }

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

    [InverseProperty("QuestionNavigation")]
    public virtual ICollection<PracticeAnswer> PracticeAnswerQuestionNavigations { get; set; } = new List<PracticeAnswer>();

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
}
