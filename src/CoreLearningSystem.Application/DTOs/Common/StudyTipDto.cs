using System;
using System.Collections.Generic;

namespace CoreLearningSystem.Application.DTOs.Common;

/// <summary>
/// Response DTO for the AI Study Tip feature.
/// Generated server-side from Skill Matrix, Weakness, Learning Path, and Goal data.
/// No external AI API is used — this is rule-based logic.
/// </summary>
public class StudyTipDto
{
    public int LearnerId { get; set; }

    /// <summary>The human-readable study tip displayed to the learner.</summary>
    public string TipText { get; set; } = string.Empty;

    /// <summary>The weakest skill identified (e.g. "Vocabulary", "Grammar"). Null if no data yet.</summary>
    public string? WeakSkill { get; set; }

    /// <summary>The most problematic topic within the weak skill (e.g. "Travel"). Null if not available.</summary>
    public string? WeakTopic { get; set; }

    /// <summary>Short call-to-action label (e.g. "Start recommended lesson", "Complete one lesson").</summary>
    public string RecommendedAction { get; set; } = string.Empty;

    /// <summary>Lesson IDs recommended for today. Empty list if no specific lesson available.</summary>
    public List<int> RecommendedLessonIds { get; set; } = new();

    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
