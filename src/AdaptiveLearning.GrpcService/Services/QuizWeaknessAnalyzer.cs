using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace AdaptiveLearning.GrpcService.Services;

public class QuizWeaknessAnalyzer : IQuizWeaknessAnalyzer
{
    public Task<QuizAnalysisResult> AnalyzeAsync(QuizAnalysisInput input)
    {
        if (input == null)
        {
            throw new ArgumentNullException(nameof(input));
        }

        var skillGroups = new Dictionary<string, List<AnswerAnalysisDetail>>(StringComparer.OrdinalIgnoreCase);

        foreach (var ans in input.Answers)
        {
            var skill = (ans.Skill ?? string.Empty).Trim();
            if (string.IsNullOrEmpty(skill))
            {
                continue;
            }

            if (!skillGroups.TryGetValue(skill, out var list))
            {
                list = new List<AnswerAnalysisDetail>();
                skillGroups[skill] = list;
            }
            list.Add(ans);
        }

        var skillScores = new List<SkillScoreResult>();

        foreach (var kvp in skillGroups)
        {
            var skillName = kvp.Key;
            var answers = kvp.Value;

            var total = answers.Count;
            var correct = answers.Count(a => a.IsCorrect);
            var incorrect = total - correct;
            var score = total > 0 ? ((double)correct / total * 100.0) : 0.0;

            skillScores.Add(new SkillScoreResult
            {
                Skill = skillName,
                Score = score,
                TotalQuestions = total,
                CorrectAnswers = correct,
                IncorrectAnswers = incorrect
            });
        }

        // Determine weakest skill
        // Rules:
        // 1. Lowest percentage score
        // 2. Highest incorrect count
        // 3. Alphabetical skill name using ordinal-insensitive comparison (StringComparer.OrdinalIgnoreCase)
        string weakestSkill = string.Empty;
        var weakTopics = new List<string>();
        string reason = "No answers provided.";

        if (skillScores.Count > 0)
        {
            var weakest = skillScores
                .OrderBy(s => s.Score)
                .ThenByDescending(s => s.IncorrectAnswers)
                .ThenBy(s => s.Skill, StringComparer.OrdinalIgnoreCase)
                .First();

            weakestSkill = weakest.Skill;

            // Get weak topics: incorrect answers belonging to the weakest skill
            var incorrectAnswersOfWeakest = skillGroups[weakestSkill]
                .Where(a => !a.IsCorrect)
                .Select(a => (a.Topic ?? string.Empty).Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            // Remove duplicates case-insensitively and sort alphabetically
            weakTopics = incorrectAnswersOfWeakest
                .GroupBy(t => t, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(t => t, StringComparer.OrdinalIgnoreCase)
                .ToList();

            reason = $"Learner answered {weakest.CorrectAnswers} of {weakest.TotalQuestions} {weakestSkill} questions correctly. {weakestSkill} is currently the weakest skill in this quiz.";
        }

        return Task.FromResult(new QuizAnalysisResult
        {
            AnalysisId = Guid.NewGuid().ToString(),
            UserId = input.UserId,
            WeakestSkill = weakestSkill,
            WeakTopics = weakTopics,
            SkillScores = skillScores.OrderBy(s => s.Skill, StringComparer.OrdinalIgnoreCase).ToList(),
            Reason = reason,
            ProcessedAt = DateTime.UtcNow.ToString("o")
        });
    }
}
