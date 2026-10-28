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

public class RecommendationNotFoundException(int recId)
    : Exception($"Recommendation {recId} was not found.")
{
    public int RecId { get; } = recId;
}
