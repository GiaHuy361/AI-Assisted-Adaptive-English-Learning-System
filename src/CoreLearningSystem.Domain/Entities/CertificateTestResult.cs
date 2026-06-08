using System;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Domain.Entities;

public class CertificateTestResult
{
    public int Id { get; set; }
    public int LearnerProfileId { get; set; }
    public CertificateType CertificateType { get; set; }
    public double Score { get; set; }
    public double MaxScore { get; set; }
    public double TargetScore { get; set; }
    public bool Passed { get; set; }
    public DateTime TakenAt { get; set; }
    public int? SourceQuizAttemptId { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    // Navigation Properties
    public LearnerProfile LearnerProfile { get; set; } = null!;
}
