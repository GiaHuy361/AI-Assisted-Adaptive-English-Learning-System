using System;
using System.Collections.Generic;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.Application.DTOs.Common;

public class SkillMatrixUpdateRequest
{
    public int UserId { get; set; }
    public int LearnerProfileId { get; set; }
    public Guid EventId { get; set; }
    public MatrixSourceType SourceType { get; set; }
    public int SourceId { get; set; }
    public List<SkillScoreDto> SkillScores { get; set; } = new();
    public List<WeakTopicDto> WeakTopics { get; set; } = new();
    public string Level { get; set; } = string.Empty;
    public DateTime OccurredAt { get; set; } = DateTime.UtcNow;
}

public class SkillScoreDto
{
    public SkillType Skill { get; set; }
    public double Score { get; set; }
    public int TotalQuestions { get; set; }
    public int CorrectAnswers { get; set; }
}

public class WeakTopicDto
{
    public SkillType Skill { get; set; }
    public string Topic { get; set; } = string.Empty;
    public string Level { get; set; } = string.Empty;
    public int IncorrectCount { get; set; }
}

public class SkillMatrixUpdateResult
{
    public int UserId { get; set; }
    public List<string> UpdatedSkills { get; set; } = new();
    public string WeakestSkill { get; set; } = string.Empty;
    public List<string> RepeatedWeakTopics { get; set; } = new();
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
