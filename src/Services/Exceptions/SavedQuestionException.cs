using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BusinessObjects;
using DataAccess;
using BusinessObjects.Entities;
using BusinessObjects.Views;
using Microsoft.EntityFrameworkCore;

namespace Services;

public class SavedQuestionException(string code, string message) : Exception(message)
{
    public const string QuestionNotFound = "question_not_found";
    public const string SavedQuestionNotFound = "saved_question_not_found";

    public string Code { get; } = code;
}
