using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Contracts;
using Microsoft.AspNetCore.Http;

namespace WebMVC.Models;

public sealed record QuestionEditorViewModel(
    AdminExamQuestionsDto QuestionsData,
    int? SelectedSectionId = null,
    int? SelectedQuestionId = null,
    string? ErrorMessage = null,
    string? SuccessMessage = null);
