using System.Collections.Generic;

namespace CoreLearningSystem.Domain.Entities;

public class AchievementBadge
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string ImageUrl { get; set; } = string.Empty;
    public string Criteria { get; set; } = string.Empty;

    // Navigation Properties
    public ICollection<LearnerBadge> AwardedLearners { get; set; } = new List<LearnerBadge>();
}
