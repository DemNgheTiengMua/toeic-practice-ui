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

public interface IWeaknessAnalysisService
{
    Task<WeaknessAnalysisView> GetWeaknessAnalysisAsync(int userId, CancellationToken cancellationToken = default);

    Task<bool> ResolveRecommendationAsync(int userId, int recId, CancellationToken cancellationToken = default);
}
