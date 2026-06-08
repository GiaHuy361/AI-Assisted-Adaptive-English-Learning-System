using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Infrastructure.Services;

/// <summary>
/// All Redis keys follow the namespace adaptive:v1 for versioning.
/// Keys must NOT contain PII (email, name, JWT, content).
/// </summary>
public sealed class CacheKeyBuilder : ICacheKeyBuilder
{
    private const string Ns = "adaptive:v1";

    // ── Lesson Keys ──────────────────────────────────────────────────────────
    public string LessonListVersion() => $"{Ns}:lessons:list-version";

    public string LessonList(long version, string? skill = null, string? level = null, string? role = null)
    {
        var s = string.IsNullOrEmpty(skill) ? "all" : skill.ToLowerInvariant();
        var l = string.IsNullOrEmpty(level) ? "all" : level.ToLowerInvariant();
        var r = string.IsNullOrEmpty(role) ? "admin" : role.ToLowerInvariant();
        return $"{Ns}:lessons:list:v{version}:{s}:{l}:{r}";
    }

    public string LessonDetail(int lessonId) => $"{Ns}:lessons:detail:{lessonId}";

    public string LessonDetailSet() => $"{Ns}:lessons:detail-keyset";

    // ── Skill Matrix ─────────────────────────────────────────────────────────
    public string SkillMatrix(int learnerProfileId) => $"{Ns}:skill-matrix:{learnerProfileId}";

    // ── Recommendations ──────────────────────────────────────────────────────
    public string ActiveRecommendations(int learnerProfileId) => $"{Ns}:recommendations:active:{learnerProfileId}";

    // ── Progress ─────────────────────────────────────────────────────────────
    public string ProgressSummary(int learnerProfileId) => $"{Ns}:progress:summary:{learnerProfileId}";
    public string ProgressDetails(int userId) => $"{Ns}:progress:details:{userId}";

    // ── Processed Events ─────────────────────────────────────────────────────
    public string ProcessedEventProcessing(string eventId) => $"{Ns}:processed-event:processing:{eventId}";
    public string ProcessedEventCompleted(string eventId) => $"{Ns}:processed-event:completed:{eventId}";
}
