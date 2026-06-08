using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.Infrastructure.Services;

public static class LearnerActivityResolver
{
    public static async Task<DateTime?> GetLatestActivityUtcAsync(AppDbContext context, int profileId, CancellationToken cancellationToken = default)
    {
        // 1. Get latest lesson completion date
        var latestLessonCompleted = await context.LearnerProgresses
            .Where(p => p.LearnerProfileId == profileId && p.IsCompleted && p.CompletedAt.HasValue)
            .Select(p => p.CompletedAt)
            .MaxAsync(cancellationToken);

        // 2. Get latest quiz attempt date
        var latestQuizAttempt = await context.QuizAttempts
            .Where(a => a.LearnerProfileId == profileId)
            .Select(a => (DateTime?)a.AttemptedAt)
            .MaxAsync(cancellationToken);

        // 3. Get latest placement test result date
        var latestPlacement = await context.PlacementTestResults
            .Where(r => r.LearnerProfileId == profileId)
            .Select(r => (DateTime?)r.TakenAt)
            .MaxAsync(cancellationToken);

        // 4. Get latest user login date (linked from profile)
        var profile = await context.LearnerProfiles
            .Include(p => p.User)
            .FirstOrDefaultAsync(p => p.Id == profileId, cancellationToken);
        
        DateTime? latestLogin = profile?.User?.LastLoginDate;

        var dates = new[] { latestLessonCompleted, latestQuizAttempt, latestPlacement, latestLogin }
            .Where(d => d.HasValue)
            .Select(d => d!.Value)
            .ToList();

        return dates.Any() ? dates.Max() : (DateTime?)null;
    }
}
