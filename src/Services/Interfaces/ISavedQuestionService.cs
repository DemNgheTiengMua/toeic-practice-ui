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

public interface ISavedQuestionService
{
    Task<SavedQuestionsView> GetSavedQuestionsAsync(int userId, SavedQuestionFilterInput filter, CancellationToken cancellationToken = default);

    Task<SavedQuestionItem> GetSavedQuestionAsync(int userId, int questionId, CancellationToken cancellationToken = default);

    Task<SaveQuestionResult> SaveQuestionAsync(int userId, int questionId, SavedQuestionSource source = SavedQuestionSource.Manual, CancellationToken cancellationToken = default);

    Task<SaveQuestionResult> UnsaveQuestionAsync(int userId, int questionId, CancellationToken cancellationToken = default);

    Task<bool> IsQuestionSavedAsync(int userId, int questionId, CancellationToken cancellationToken = default);
}
