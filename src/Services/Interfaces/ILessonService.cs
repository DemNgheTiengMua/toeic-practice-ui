using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Contracts;
using DataAccess;
using Microsoft.EntityFrameworkCore;

namespace Services;

public interface ILessonService
{
    Task<LessonDetailDto?> GetLessonAsync(int lessonId, int certId);
    Task<LessonProgressDto> GetProgressAsync(int userId, int lessonId);
    Task RecordCardViewAsync(int userId, int lessonId, int cardId);
    Task<LessonDetailDto?> GetLessonByCategoryAsync(int categoryId, int certId);
}
