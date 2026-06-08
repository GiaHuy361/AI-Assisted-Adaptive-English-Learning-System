using System;
using System.Threading.Tasks;
using CoreLearningSystem.Domain.Entities;

namespace CoreLearningSystem.Application.Interfaces;

public interface IRecommendationAnalyticsService
{
    Task<RecommendationStatisticSnapshot> ComputeAndSaveSnapshotAsync(DateTime periodStart, DateTime periodEnd);
}
