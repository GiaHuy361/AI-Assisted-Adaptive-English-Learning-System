using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.DTOs.Events;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Application.Features.Grading;

public class UserAnswerDto
{
    public int QuestionId { get; set; }
    public int AnswerOptionId { get; set; }
    public int SelectedAnswerId { get; set; }
    public int SelectedAnswerOptionId { get; set; }
    public int SelectedOptionIndex { get; set; } = -1;
}

public class SubmitQuizDto
{
    public int QuizId { get; set; }
    public List<UserAnswerDto> Answers { get; set; } = new();
}

public record QuizAttemptResponseDto(int AttemptId, double Score, int CorrectCount, int IncorrectCount, bool IsPassed, string AssignedLevel);

// SUBMIT QUIZ COMMAND
public record SubmitQuizAttemptCommand(SubmitQuizDto Dto, int LearnerId) : IRequest<ApiResponse<QuizAttemptResponseDto>>;

public class SubmitQuizAttemptCommandHandler : IRequestHandler<SubmitQuizAttemptCommand, ApiResponse<QuizAttemptResponseDto>>
{
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<LearnerProfile> _learnerRepository;
    private readonly IRepository<Question> _questionRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;
    private readonly IRepository<AnswerOption> _answerOptionRepository;
    private readonly IRepository<QuizAttemptDetail> _detailRepository;
    private readonly IKafkaPublisher _kafkaPublisher;
    private readonly IRepository<Lesson> _lessonRepository;
    private readonly IRepository<LearnerProgress> _progressRepository;

    public SubmitQuizAttemptCommandHandler(
        IRepository<Quiz> quizRepository,
        IRepository<LearnerProfile> learnerRepository,
        IRepository<Question> questionRepository,
        IRepository<QuizAttempt> attemptRepository,
        IRepository<AnswerOption> answerOptionRepository,
        IRepository<QuizAttemptDetail> detailRepository,
        IKafkaPublisher kafkaPublisher,
        IRepository<Lesson> lessonRepository,
        IRepository<LearnerProgress> progressRepository)
    {
        _quizRepository = quizRepository;
        _learnerRepository = learnerRepository;
        _questionRepository = questionRepository;
        _attemptRepository = attemptRepository;
        _answerOptionRepository = answerOptionRepository;
        _detailRepository = detailRepository;
        _kafkaPublisher = kafkaPublisher;
        _lessonRepository = lessonRepository;
        _progressRepository = progressRepository;
    }

    public async Task<ApiResponse<QuizAttemptResponseDto>> Handle(SubmitQuizAttemptCommand request, CancellationToken cancellationToken)
    {
        if (request.Dto == null)
        {
            return ApiResponse<QuizAttemptResponseDto>.FailureResponse("Dữ liệu gửi lên không hợp lệ.");
        }

        if (request.Dto.Answers == null)
        {
            request.Dto.Answers = new List<UserAnswerDto>();
        }

        Console.WriteLine($"Processing Quiz ID: {request.Dto.QuizId} with {request.Dto.Answers.Count} answers.");

        var quiz = await _quizRepository.GetByIdAsync(request.Dto.QuizId);
        if (quiz == null) 
        {
            Console.WriteLine($"Quiz not found mismatch: {request.Dto.QuizId}");
            return ApiResponse<QuizAttemptResponseDto>.FailureResponse($"Bài trắc nghiệm số {request.Dto.QuizId} không tồn tại trên hệ thống.");
        }

        var learners = await _learnerRepository.FindAsync(l => l.UserId == request.LearnerId);
        var learner = learners.FirstOrDefault();
        if (learner == null) 
        {
            Console.WriteLine($"[CRITICAL EXCEPTION GUARD] Submission failed. LearnerProfile record is missing in the database for UserId: {request.LearnerId}");
            return ApiResponse<QuizAttemptResponseDto>.FailureResponse("Hồ sơ học viên không tồn tại hoặc dữ liệu tài khoản bị lệch pha. Vui lòng đăng xuất và đăng nhập lại để làm mới phiên.");
        }

        // Retrieve existing attempt if any using the profile's primary key (Id)
        var existingAttempts = await _attemptRepository.FindAsync(a => a.QuizId == request.Dto.QuizId && a.LearnerProfileId == learner.Id);
        var existingAttemptsList = existingAttempts.ToList();
        var existingAttempt = existingAttemptsList.FirstOrDefault();

        if ((quiz.IsPlacementTest || quiz.Level == EnglishLevel.PlacementTest) && existingAttempt != null)
        {
            Console.WriteLine($"Placement test already taken by LearnerId: {learner.Id}");
            return ApiResponse<QuizAttemptResponseDto>.FailureResponse("Bạn đã thực hiện bài kiểm tra đánh giá năng lực đầu vào này rồi. Mỗi tài khoản chỉ được phép thực hiện một lần duy nhất.");
        }

        if (!(quiz.IsPlacementTest || quiz.Level == EnglishLevel.PlacementTest))
        {
            // Guard: If already passed
            bool hasPassedBefore = existingAttemptsList.Any(a => a.IsPassed);
            if (hasPassedBefore)
            {
                return ApiResponse<QuizAttemptResponseDto>.FailureResponse("Bạn đã hoàn thành và vượt qua bài trắc nghiệm này rồi. Không thể làm lại.");
            }

            // Guard: If failed before, must study again first
            if (existingAttemptsList.Any())
            {
                var lessons = await _lessonRepository.FindAsync(l => l.QuizId == quiz.Id);
                var lesson = lessons.FirstOrDefault();
                if (lesson != null)
                {
                    var progresses = await _progressRepository.FindAsync(p => p.LearnerProfileId == learner.Id && p.LessonId == lesson.Id);
                    var progress = progresses.FirstOrDefault();
                    
                    var latestAttempt = existingAttemptsList.OrderByDescending(a => a.AttemptedAt).First();
                    
                    if (progress == null || !progress.IsCompleted || (progress.CompletedAt.HasValue && latestAttempt.AttemptedAt > progress.CompletedAt.Value))
                    {
                        return ApiResponse<QuizAttemptResponseDto>.FailureResponse("Bạn cần phải học lại lý thuyết và nhấn 'Đánh dấu hoàn thành' bài học trước khi có thể kiểm tra lại.");
                    }
                }
            }
        }

        // Fetch all questions related to this quiz
        var questions = (await _questionRepository.FindAsync(q => q.QuizId == request.Dto.QuizId)).ToList();
        var questionIds = questions.Select(q => q.Id).ToList();

        // VALIDATION GUARD: check if answers matches the configured quiz questions
        bool hasMismatch = request.Dto.Answers.Any(a => !questionIds.Contains(a.QuestionId));
        if (hasMismatch || request.Dto.Answers.Count == 0 || request.Dto.Answers.Count != questions.Count)
        {
            Console.WriteLine($"Mismatch detected! Submitted answers: {request.Dto.Answers.Count}, Quiz questions: {questions.Count}");
            return ApiResponse<QuizAttemptResponseDto>.FailureResponse("Dữ liệu câu hỏi gửi lên không trùng khớp với cấu trúc bài thi cấu hình trên hệ thống.");
        }

        var allOptions = await _answerOptionRepository.FindAsync(o => questionIds.Contains(o.QuestionId));
        var optionsByQuestion = allOptions
            .GroupBy(o => o.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Id).ToList());
        
        int correctCount = 0;
        int incorrectCount = 0;
        Console.WriteLine($"[GRADING DEBUG] Total answers submitted by client: {request.Dto.Answers.Count}");

        var attemptDetails = new List<QuizAttemptDetail>();

        foreach (var answerDto in request.Dto.Answers)
        {
            // Safety: coalesce SelectedAnswerOptionId if not set
            if (answerDto.SelectedAnswerOptionId == 0 && answerDto.SelectedAnswerId > 0)
            {
                answerDto.SelectedAnswerOptionId = answerDto.SelectedAnswerId;
            }
            if (answerDto.SelectedAnswerOptionId == 0 && answerDto.AnswerOptionId > 0)
            {
                answerDto.SelectedAnswerOptionId = answerDto.AnswerOptionId;
            }

            AnswerOption? selectedOption = null;
            AnswerOption? correctOption = null;

            if (optionsByQuestion.TryGetValue(answerDto.QuestionId, out var optionsList))
            {
                correctOption = optionsList.FirstOrDefault(o => o.IsCorrect);
                
                // 1. Try finding by database ID first
                selectedOption = optionsList.FirstOrDefault(o => o.Id == answerDto.SelectedAnswerOptionId);

                // 2. Fallback to index-based lookup
                if (selectedOption == null)
                {
                    int index = answerDto.SelectedOptionIndex >= 0 ? answerDto.SelectedOptionIndex : answerDto.SelectedAnswerOptionId;
                    if (index >= 0 && index < optionsList.Count)
                    {
                        selectedOption = optionsList[index];
                    }
                }
            }

            if (selectedOption == null)
            {
                Console.WriteLine($"[GRADING WARNING] Submitted Option ID/Index {answerDto.SelectedAnswerOptionId} / {answerDto.SelectedOptionIndex} does not exist for Question {answerDto.QuestionId}!");
                incorrectCount++;
                attemptDetails.Add(new QuizAttemptDetail
                {
                    QuestionId = answerDto.QuestionId,
                    SelectedAnswerOptionId = null,
                    IsCorrect = false
                });
                continue;
            }

            // 2. Double check if this option belongs to the question and matches the correct option ID
            bool isCorrect = selectedOption.QuestionId == answerDto.QuestionId && correctOption != null && selectedOption.Id == correctOption.Id;

            if (isCorrect)
            {
                correctCount++;
                Console.WriteLine($"[GRADING MATCH] Question {answerDto.QuestionId}: Option {selectedOption.Id} (Text: {selectedOption.OptionText}) is CORRECT.");
            }
            else
            {
                incorrectCount++;
                Console.WriteLine($"[GRADING MISMATCH] Question {answerDto.QuestionId}: Option {selectedOption.Id} (Text: {selectedOption.OptionText}) is INCORRECT.");
            }

            attemptDetails.Add(new QuizAttemptDetail
            {
                QuestionId = answerDto.QuestionId,
                SelectedAnswerOptionId = selectedOption.Id,
                IsCorrect = isCorrect
            });
        }

        Console.WriteLine($"[GRADING FINAL SCORE] Calculated Score: {correctCount}/{request.Dto.Answers.Count}");

        double totalQuestions = questions.Count > 0 ? questions.Count : 1;
        double score = ((double)correctCount / totalQuestions) * 100.0;
        bool isPassed = score >= quiz.PassingScore;

        var attempt = new QuizAttempt
        {
            QuizId = request.Dto.QuizId,
            LearnerProfileId = learner.Id,
            Score = score,
            CorrectAnswersCount = correctCount,
            IncorrectAnswersCount = incorrectCount,
            IsPassed = isPassed,
            AttemptedAt = DateTime.UtcNow,
            Details = attemptDetails
        };

        await _attemptRepository.AddAsync(attempt);
        await _attemptRepository.SaveChangesAsync();

        int finalAttemptId = attempt.Id;

        // 2. CRITICAL FIX: Only run adaptive level re-calculation if it is a placement test!
        if (quiz.IsPlacementTest || quiz.Level == EnglishLevel.PlacementTest)
        {
            Console.WriteLine($"[PLACEMENT PROCESSING] Re-calculating proficiency for profile: {learner.Id}");
            var calculatedLevel = DetermineCEFRLevel(correctCount, request.Dto.Answers.Count);
            learner.Level = calculatedLevel;
            learner.LastActiveAt = DateTime.UtcNow;
            await _learnerRepository.UpdateAsync(learner);
            await _learnerRepository.SaveChangesAsync();
        }
        else
        {
            Console.WriteLine($"[STANDARD QUIZ PROCESSING] Skipping profile leveling for standard quiz ID: {quiz.Id}");
            // Standard Quiz check for level promotion
            await CheckAndPromoteUserLevelAsync(_lessonRepository, _progressRepository, learner, cancellationToken);
        }

        // Fire event via Kafka
        var ev = new QuizSubmittedEvent(finalAttemptId, quiz.Id, learner.Id, score, isPassed, DateTime.UtcNow);
        await _kafkaPublisher.PublishQuizSubmittedAsync(ev);

        var response = new QuizAttemptResponseDto(finalAttemptId, score, correctCount, incorrectCount, isPassed, learner.Level.ToString());
        return ApiResponse<QuizAttemptResponseDto>.SuccessResponse(response, "Quiz graded successfully.");
    }

    private async Task CheckAndPromoteUserLevelAsync(
        IRepository<Lesson> lessonRepository,
        IRepository<LearnerProgress> progressRepository,
        LearnerProfile learner,
        CancellationToken cancellationToken)
    {
        var currentLevel = learner.Level;
        if (currentLevel == EnglishLevel.PlacementTest || currentLevel == EnglishLevel.None) return;

        var lessonsInTier = await lessonRepository.FindAsync(l => l.Level == currentLevel && l.Status == LessonStatus.Published);
        var lessonsInTierList = lessonsInTier.ToList();

        if (lessonsInTierList.Count == 0) return;

        foreach (var lesson in lessonsInTierList)
        {
            var progresses = await progressRepository.FindAsync(p => p.LearnerProfileId == learner.Id && p.LessonId == lesson.Id && p.IsCompleted);
            var progress = progresses.FirstOrDefault();
            
            if (progress == null) return;

            if (lesson.QuizId.HasValue)
            {
                var completedAt = progress.CompletedAt ?? DateTime.MinValue;
                var attempts = await _attemptRepository.FindAsync(a => a.LearnerProfileId == learner.Id 
                                                                    && a.QuizId == lesson.QuizId.Value 
                                                                    && (a.Score >= 50.0 || a.IsPassed)
                                                                    && a.AttemptedAt >= completedAt.AddSeconds(-10));
                var isQuizPassed = attempts.Any();

                if (!isQuizPassed) return;
            }
        }

        EnglishLevel nextLevel = currentLevel switch
        {
            EnglishLevel.A1 => EnglishLevel.A2,
            EnglishLevel.A2 => EnglishLevel.B1,
            EnglishLevel.B1 => EnglishLevel.B2,
            EnglishLevel.B2 => EnglishLevel.C1,
            EnglishLevel.C1 => EnglishLevel.C2,
            _ => currentLevel
        };

        if (nextLevel != currentLevel)
        {
            learner.Level = nextLevel;
            learner.LastActiveAt = DateTime.UtcNow;
            await _learnerRepository.UpdateAsync(learner);
            await _learnerRepository.SaveChangesAsync();
            Console.WriteLine($"[ACADEMIC BLUEPRINT LEVEL UP] Learner {learner.Id} promoted from {currentLevel} to {nextLevel}!");
        }
    }

    private EnglishLevel DetermineCEFRLevel(int correctCount, int totalCount)
    {
        Console.WriteLine($"[PROCESSING PLACEMENT] Total correct answers: {correctCount}");

        // Clear boundary assignment to prevent any runtime translation errors
        return correctCount switch
        {
            0 => EnglishLevel.A1,
            1 => EnglishLevel.A1,
            2 => EnglishLevel.A2,
            3 => EnglishLevel.B1,
            4 => EnglishLevel.B2,
            5 => EnglishLevel.C1,
            _ => EnglishLevel.A1 // Safe fallback
        };
    }
}
