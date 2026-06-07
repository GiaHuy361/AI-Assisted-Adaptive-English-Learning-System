using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Features.Auth;

namespace CoreLearningSystem.Infrastructure.Persistence;

public static class DataSeeder
{
    public static async Task SeedAsync(AppDbContext context)
    {
        // 1. Ensure database schema exists via EF Core Migrations
        await context.Database.MigrateAsync();


        // 3. Seed Users
        if (!await context.Users.AnyAsync())
        {
            // Seed Admin Account
            var adminUser = new User
            {
                Username = "admin",
                Email = "admin@adaptivelearn.com",
                FullName = "System Administrator",
                PasswordHash = BCryptMock.HashPassword("Admin@123"),
                Role = UserRole.Admin,
                CreatedAt = DateTime.UtcNow
            };
            await context.Users.AddAsync(adminUser);

            // Seed Learner Account with Profile
            var learnerUser = new User
            {
                Username = "learner",
                Email = "learner@adaptivelearn.com",
                FullName = "John Doe Learner",
                PasswordHash = BCryptMock.HashPassword("Learner@123"),
                Role = UserRole.Learner,
                CreatedAt = DateTime.UtcNow,
                LearnerProfile = new LearnerProfile
                {
                    Level = EnglishLevel.A1,
                    ActivityStatus = ActivityStatus.Active,
                    LastActiveAt = DateTime.UtcNow
                }
            };
            await context.Users.AddAsync(learnerUser);
            await context.SaveChangesAsync();
        }

        // 4. Seed Diagnostic Placement Test Quiz & CEFR Levels ONLY if no quizzes exist
        if (!await context.Quizzes.AnyAsync())
        {
            // 5. Seed Diagnostic Placement Test Quiz
            var placementTest = new Quiz
            {
                Title = "Diagnostic Placement Test",
                Description = "Determine your optimal entry level in the system.",
                DurationMinutes = 30,
                PassingScore = 50.0,
                Level = EnglishLevel.PlacementTest,
                IsPlacementTest = true,
                CreatedAt = DateTime.UtcNow,
                Questions = new List<Question>
                {
                    new Question
                    {
                        Content = "She ___ to school every day.",
                        Skill = SkillType.Grammar,
                        Topic = "Present Simple",
                        Level = EnglishLevel.PlacementTest,
                        CorrectAnswer = "goes",
                        Explanation = "Third-person singular subject takes 'goes'.",
                        Score = 2.0,
                        AnswerOptions = new List<AnswerOption>
                        {
                            new AnswerOption { OptionText = "go", IsCorrect = false },
                            new AnswerOption { OptionText = "goes", IsCorrect = true },
                            new AnswerOption { OptionText = "going", IsCorrect = false },
                            new AnswerOption { OptionText = "gone", IsCorrect = false }
                        }
                    },
                    new Question
                    {
                        Content = "Yesterday, I ___ a beautiful movie.",
                        Skill = SkillType.Vocabulary,
                        Topic = "Past Simple",
                        Level = EnglishLevel.PlacementTest,
                        CorrectAnswer = "watched",
                        Explanation = "Simple past form of watch is 'watched'.",
                        Score = 2.0,
                        AnswerOptions = new List<AnswerOption>
                        {
                            new AnswerOption { OptionText = "watch", IsCorrect = false },
                            new AnswerOption { OptionText = "watched", IsCorrect = true },
                            new AnswerOption { OptionText = "watching", IsCorrect = false },
                            new AnswerOption { OptionText = "watches", IsCorrect = false }
                        }
                    },
                    new Question
                    {
                        Content = "If it rains tomorrow, we ___ the picnic.",
                        Skill = SkillType.Grammar,
                        Topic = "Conditionals",
                        Level = EnglishLevel.PlacementTest,
                        CorrectAnswer = "will cancel",
                        Explanation = "First conditional structure uses simple present in if-clause and simple future in main clause.",
                        Score = 2.0,
                        AnswerOptions = new List<AnswerOption>
                        {
                            new AnswerOption { OptionText = "cancel", IsCorrect = false },
                            new AnswerOption { OptionText = "will cancel", IsCorrect = true },
                            new AnswerOption { OptionText = "canceled", IsCorrect = false },
                            new AnswerOption { OptionText = "would cancel", IsCorrect = false }
                        }
                    },
                    new Question
                    {
                        Content = "By the time he arrived, she ___ already left.",
                        Skill = SkillType.Grammar,
                        Topic = "Past Perfect",
                        Level = EnglishLevel.PlacementTest,
                        CorrectAnswer = "had",
                        Explanation = "Past perfect tense uses had + past participle to denote completed action before another past point.",
                        Score = 2.0,
                        AnswerOptions = new List<AnswerOption>
                        {
                            new AnswerOption { OptionText = "has", IsCorrect = false },
                            new AnswerOption { OptionText = "have", IsCorrect = false },
                            new AnswerOption { OptionText = "had", IsCorrect = true },
                            new AnswerOption { OptionText = "will have", IsCorrect = false }
                        }
                    },
                    new Question
                    {
                        Content = "He spoke so quickly that I could ___ understand what he said.",
                        Skill = SkillType.Reading,
                        Topic = "Adverbs",
                        Level = EnglishLevel.PlacementTest,
                        CorrectAnswer = "hardly",
                        Explanation = "'Hardly' acts as a negative adverb meaning 'almost not'.",
                        Score = 2.0,
                        AnswerOptions = new List<AnswerOption>
                        {
                            new AnswerOption { OptionText = "hard", IsCorrect = false },
                            new AnswerOption { OptionText = "hardly", IsCorrect = true },
                            new AnswerOption { OptionText = "easy", IsCorrect = false },
                            new AnswerOption { OptionText = "easily", IsCorrect = false }
                        }
                    }
                }
            };
            await context.Quizzes.AddAsync(placementTest);

            // 6. Seed exactly ONE Quiz and TWO Lessons for each of the 6 CEFR English Levels
            var levelDataList = new List<LevelSeedData>
            {
                new LevelSeedData
                {
                    Level = EnglishLevel.A1,
                    Lessons = new List<LessonSeed>
                    {
                        new LessonSeed { Title = "Present Simple Basics", Content = "We use the Present Simple to talk about habits, routines and general truths. Example: She works as a teacher.", Skill = SkillType.Grammar, Topic = "Present Simple", Duration = 15 },
                        new LessonSeed { Title = "Basic Introductions", Content = "Learn how to introduce yourself and others. Key phrases: 'Hello, my name is John. Nice to meet you.'", Skill = SkillType.Speaking, Topic = "Introductions", Duration = 20 }
                    },
                    Questions = new List<QuestionSeed>
                    {
                        new QuestionSeed { Content = "What ___ your name?", CorrectAnswer = "is", Options = new[] { "am", "is", "are", "be" }, Explanation = "With singular subject 'your name', use the singular verb 'is'." },
                        new QuestionSeed { Content = "She ___ to school every day.", CorrectAnswer = "goes", Options = new[] { "go", "goes", "going", "gone" }, Explanation = "Third-person singular subject 'She' takes 'goes'." },
                        new QuestionSeed { Content = "I ___ a student.", CorrectAnswer = "am", Options = new[] { "am", "is", "are", "be" }, Explanation = "First-person singular subject 'I' takes 'am'." },
                        new QuestionSeed { Content = "They ___ tennis on Sundays.", CorrectAnswer = "play", Options = new[] { "play", "plays", "playing", "player" }, Explanation = "Third-person plural subject 'They' takes the base verb 'play'." },
                        new QuestionSeed { Content = "Where ___ you from?", CorrectAnswer = "are", Options = new[] { "am", "is", "are", "be" }, Explanation = "Second-person subject 'you' takes the plural verb 'are'." }
                    }
                },
                new LevelSeedData
                {
                    Level = EnglishLevel.A2,
                    Lessons = new List<LessonSeed>
                    {
                        new LessonSeed { Title = "Past Simple Regulars", Content = "Use the Past Simple to talk about completed actions in the past. Regular verbs add -ed. Example: I visited London last year.", Skill = SkillType.Grammar, Topic = "Past Simple", Duration = 15 },
                        new LessonSeed { Title = "Describing Places", Content = "Use adjectives and prepositions to describe cities and neighborhoods. Example: The park is near my house.", Skill = SkillType.Reading, Topic = "Adjectives", Duration = 20 }
                    },
                    Questions = new List<QuestionSeed>
                    {
                        new QuestionSeed { Content = "Yesterday, we ___ a new movie.", CorrectAnswer = "watched", Options = new[] { "watch", "watched", "watching", "watches" }, Explanation = "For past completed actions, use past simple 'watched'." },
                        new QuestionSeed { Content = "The hotel was ___ than we expected.", CorrectAnswer = "cheaper", Options = new[] { "cheap", "cheaper", "cheapest", "more cheap" }, Explanation = "Use comparative form 'cheaper' when comparing two things." },
                        new QuestionSeed { Content = "She ___ at 8 AM yesterday.", CorrectAnswer = "woke up", Options = new[] { "wake up", "woke up", "woken up", "wakes up" }, Explanation = "Simple past form of wake up is 'woke up'." },
                        new QuestionSeed { Content = "We went to the beach ___ Saturday.", CorrectAnswer = "on", Options = new[] { "in", "on", "at", "for" }, Explanation = "Use preposition 'on' for specific days of the week." },
                        new QuestionSeed { Content = "Have you ___ eaten sushi?", CorrectAnswer = "ever", Options = new[] { "ever", "never", "yet", "already" }, Explanation = "Use 'ever' in present perfect questions to mean 'at any time in your life'." }
                    }
                },
                new LevelSeedData
                {
                    Level = EnglishLevel.B1,
                    Lessons = new List<LessonSeed>
                    {
                        new LessonSeed { Title = "Present Perfect vs Past Simple", Content = "Use Present Perfect for life experiences without a specific time. Use Past Simple for finished events with a time marker.", Skill = SkillType.Grammar, Topic = "Present Perfect", Duration = 15 },
                        new LessonSeed { Title = "Giving Advice and Suggestions", Content = "Use modal verbs like 'should', 'ought to', and 'had better' to give recommendations. Example: You should see a doctor.", Skill = SkillType.Listening, Topic = "Modal Verbs", Duration = 20 }
                    },
                    Questions = new List<QuestionSeed>
                    {
                        new QuestionSeed { Content = "I ___ lived here since 2015.", CorrectAnswer = "have", Options = new[] { "am", "have", "has", "was" }, Explanation = "Use present perfect 'have lived' for action starting in the past continuing to present." },
                        new QuestionSeed { Content = "If I ___ you, I would take the offer.", CorrectAnswer = "were", Options = new[] { "am", "was", "were", "be" }, Explanation = "Use subjunctive 'were' in hypothetical conditionals." },
                        new QuestionSeed { Content = "He doesn't mind ___ early in the morning.", CorrectAnswer = "waking up", Options = new[] { "to wake up", "waking up", "wake up", "woke up" }, Explanation = "Verb 'mind' is followed by a gerund (-ing)." },
                        new QuestionSeed { Content = "This book was ___ by a famous author.", CorrectAnswer = "written", Options = new[] { "write", "wrote", "written", "writing" }, Explanation = "Use past participle 'written' in passive voice construction." },
                        new QuestionSeed { Content = "You ___ study harder if you want to pass.", CorrectAnswer = "should", Options = new[] { "should", "ought", "would", "had" }, Explanation = "Use 'should' for recommendations. ('Ought' requires 'to')." }
                    }
                },
                new LevelSeedData
                {
                    Level = EnglishLevel.B2,
                    Lessons = new List<LessonSeed>
                    {
                        new LessonSeed { Title = "First and Second Conditionals", Content = "First Conditional: real future possibilities (If it rains, we will stay). Second Conditional: imaginary present/future (If I won the lottery, I would buy a car).", Skill = SkillType.Grammar, Topic = "Conditionals", Duration = 15 },
                        new LessonSeed { Title = "Passive Voice in Business", Content = "Use the passive voice to focus on the action rather than the doer. Example: The project was completed on schedule.", Skill = SkillType.Writing, Topic = "Passive Voice", Duration = 20 }
                    },
                    Questions = new List<QuestionSeed>
                    {
                        new QuestionSeed { Content = "The decision ___ made by the board tomorrow.", CorrectAnswer = "will be", Options = new[] { "will", "will be", "is", "was" }, Explanation = "Future passive voice uses 'will be' + past participle." },
                        new QuestionSeed { Content = "I wish I ___ more time to finish this.", CorrectAnswer = "had", Options = new[] { "have", "had", "would have", "will have" }, Explanation = "Wish about the present uses past simple 'had'." },
                        new QuestionSeed { Content = "She admitted ___ the confidential document.", CorrectAnswer = "reading", Options = new[] { "to read", "reading", "read", "having readed" }, Explanation = "Verb 'admit' is followed by a gerund (-ing)." },
                        new QuestionSeed { Content = "Despite ___ tired, he finished the work.", CorrectAnswer = "being", Options = new[] { "he was", "being", "of being", "his" }, Explanation = "Preposition 'despite' is followed by a noun or gerund (-ing)." },
                        new QuestionSeed { Content = "The police are looking ___ the cause of the fire.", CorrectAnswer = "into", Options = new[] { "into", "for", "after", "at" }, Explanation = "Phrasal verb 'look into' means to investigate." }
                    }
                },
                new LevelSeedData
                {
                    Level = EnglishLevel.C1,
                    Lessons = new List<LessonSeed>
                    {
                        new LessonSeed { Title = "Mixed Conditionals", Content = "Mixed conditionals combine different times in the if-clause and result-clause. Example: If I had studied harder (past), I would be rich now (present).", Skill = SkillType.Grammar, Topic = "Mixed Conditionals", Duration = 15 },
                        new LessonSeed { Title = "Advanced Cleft Sentences", Content = "Cleft sentences emphasize a specific part of the sentence. Example: What surprised me most was his sudden resignation.", Skill = SkillType.Reading, Topic = "Cleft Sentences", Duration = 20 }
                    },
                    Questions = new List<QuestionSeed>
                    {
                        new QuestionSeed { Content = "Hardly ___ entered the room when the phone rang.", CorrectAnswer = "had I", Options = new[] { "I had", "had I", "did I", "I did" }, Explanation = "Inversion is required after restrictive adverbs like 'Hardly' starting a sentence." },
                        new QuestionSeed { Content = "It's high time you ___ looking for a job.", CorrectAnswer = "started", Options = new[] { "start", "started", "starting", "would start" }, Explanation = "Expression 'It's high time' takes past simple to express urgency." },
                        new QuestionSeed { Content = "He spoke ___ he knew all the answers.", CorrectAnswer = "as if", Options = new[] { "as if", "even though", "so that", "in case" }, Explanation = "Use 'as if' or 'as though' to describe an imaginary state." },
                        new QuestionSeed { Content = "I would rather you ___ tell anyone about this.", CorrectAnswer = "didn't", Options = new[] { "don't", "didn't", "not", "won't" }, Explanation = "Subject-change preference ('would rather you') takes simple past." },
                        new QuestionSeed { Content = "Under no circumstances ___ you open this door.", CorrectAnswer = "should you", Options = new[] { "must", "should you", "you should", "you must" }, Explanation = "Under no circumstances starts a sentence, requiring inversion." }
                    }
                },
                new LevelSeedData
                {
                    Level = EnglishLevel.C2,
                    Lessons = new List<LessonSeed>
                    {
                        new LessonSeed { Title = "Subjunctive Mood Nuances", Content = "The subjunctive mood expresses demands, suggestions, or hypothetical situations. Example: It is essential that he be present.", Skill = SkillType.Grammar, Topic = "Subjunctive", Duration = 15 },
                        new LessonSeed { Title = "Inversion with Negative Adverbs", Content = "Inversion after negative or restrictive adverbs adds emphasis. Example: Seldom have I witnessed such dedication.", Skill = SkillType.Grammar, Topic = "Inversion", Duration = 20 }
                    },
                    Questions = new List<QuestionSeed>
                    {
                        new QuestionSeed { Content = "Seldom ___ seen such a brilliant performance.", CorrectAnswer = "have we", Options = new[] { "we have", "have we", "did we", "we did" }, Explanation = "Sentence-initial negative adverb 'seldom' requires inversion." },
                        new QuestionSeed { Content = "The committee recommended that the proposal ___ postponed.", CorrectAnswer = "be", Options = new[] { "is", "be", "was", "should" }, Explanation = "Recommendations take the subjunctive base form 'be'." },
                        new QuestionSeed { Content = "Were it ___ for your help, I would have failed.", CorrectAnswer = "not", Options = new[] { "not", "never", "without", "no" }, Explanation = "Condition inversion formula is 'Were it not for...'" },
                        new QuestionSeed { Content = "He is reputedly ___ wealthy man in the city.", CorrectAnswer = "the most", Options = new[] { "the most", "most", "extremely", "very" }, Explanation = "Superlative expression is 'the most wealthy'." },
                        new QuestionSeed { Content = "No sooner had he left ___ it started pouring.", CorrectAnswer = "than", Options = new[] { "when", "than", "then", "that" }, Explanation = "Formula 'No sooner had ... than ...' is used for successive events." }
                    }
                }
            };

            foreach (var data in levelDataList)
            {
                // Seed 1 Quiz (IsPlacementTest = false)
                var quiz = new Quiz
                {
                    Title = $"{data.Level} Standard Evaluation Quiz",
                    Description = $"Test assessment matching the CEFR {data.Level} guidelines.",
                    DurationMinutes = 15,
                    PassingScore = 60.0,
                    Level = data.Level,
                    IsPlacementTest = false,
                    CreatedAt = DateTime.UtcNow,
                    Questions = new List<Question>()
                };

                foreach (var qSeed in data.Questions)
                {
                    var question = new Question
                    {
                        Content = qSeed.Content,
                        Skill = SkillType.Grammar,
                        Topic = "Evaluation",
                        Level = data.Level,
                        CorrectAnswer = qSeed.CorrectAnswer,
                        Explanation = qSeed.Explanation,
                        Score = 2.0,
                        AnswerOptions = new List<AnswerOption>()
                    };

                    foreach (var optText in qSeed.Options)
                    {
                        question.AnswerOptions.Add(new AnswerOption
                        {
                            OptionText = optText,
                            IsCorrect = optText == qSeed.CorrectAnswer
                        });
                    }

                    quiz.Questions.Add(question);
                }

                await context.Quizzes.AddAsync(quiz);
                await context.SaveChangesAsync();

                // Seed two lessons for this level
                foreach (var lSeed in data.Lessons)
                {
                    var lesson = new Lesson
                    {
                        Title = lSeed.Title,
                        Content = lSeed.Content,
                        Skill = lSeed.Skill,
                        Topic = lSeed.Topic,
                        Level = data.Level,
                        DurationInMinutes = lSeed.Duration,
                        Status = LessonStatus.Published,
                        QuizId = quiz.Id, // Link to the level quiz
                        CreatedAt = DateTime.UtcNow
                    };

                    await context.Lessons.AddAsync(lesson);
                }

                await context.SaveChangesAsync();
            }
        }
    }
}

public class LevelSeedData
{
    public EnglishLevel Level { get; set; }
    public List<LessonSeed> Lessons { get; set; } = new();
    public List<QuestionSeed> Questions { get; set; } = new();
}

public class LessonSeed
{
    public string Title { get; set; } = "";
    public string Content { get; set; } = "";
    public SkillType Skill { get; set; }
    public string Topic { get; set; } = "";
    public int Duration { get; set; }
}

public class QuestionSeed
{
    public string Content { get; set; } = "";
    public string CorrectAnswer { get; set; } = "";
    public string[] Options { get; set; } = Array.Empty<string>();
    public string Explanation { get; set; } = "";
}
