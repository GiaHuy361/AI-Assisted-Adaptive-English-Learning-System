using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class SkillMatrixHistory
{
    public int Id { get; set; }
    public int SkillMatrixId { get; set; }
    public int LearnerProfileId { get; set; }
    public SkillType Skill { get; set; }
    public double PreviousScore { get; set; }
    public double AssessmentScore { get; set; }
    public double NewScore { get; set; }
    public MatrixSourceType SourceType { get; set; }
    public int SourceId { get; set; }
    public Guid EventId { get; set; }
    public string Reason { get; set; } = string.Empty;
    public string? DecayPeriodKey { get; set; }
    public DateTime RecordedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
}
