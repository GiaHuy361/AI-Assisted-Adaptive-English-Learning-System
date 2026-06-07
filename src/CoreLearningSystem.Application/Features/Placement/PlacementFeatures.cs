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

namespace CoreLearningSystem.Application.Features.Placement;

public class PlacementAnswerInput
{
    public int QuestionId { get; set; }
    public int SelectedOptionIndex { get; set; }
}

public record PlacementQuestionDto(int Id, string Text, List<string> Options);
public record PlacementSubmitResponse(int Score, string CefrLevel, string Recommendation);

// START TEST
public record StartPlacementTestCommand(int LearnerId) : IRequest<ApiResponse<List<PlacementQuestionDto>>>;

public class StartPlacementTestCommandHandler : IRequestHandler<StartPlacementTestCommand, ApiResponse<List<PlacementQuestionDto>>>
{
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<Question> _questionRepository;
    private readonly IRepository<AnswerOption> _answerOptionRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;
    private readonly IRepository<PlacementTestResult> _testResultRepository;
    private readonly IRepository<LearnerProfile> _learnerRepository;

    public StartPlacementTestCommandHandler(
        IRepository<Quiz> quizRepository,
        IRepository<Question> questionRepository,
        IRepository<AnswerOption> answerOptionRepository,
        IRepository<QuizAttempt> attemptRepository,
        IRepository<PlacementTestResult> testResultRepository,
        IRepository<LearnerProfile> learnerRepository)
    {
        _quizRepository = quizRepository;
        _questionRepository = questionRepository;
        _answerOptionRepository = answerOptionRepository;
        _attemptRepository = attemptRepository;
        _testResultRepository = testResultRepository;
        _learnerRepository = learnerRepository;
    }

    public async Task<ApiResponse<List<PlacementQuestionDto>>> Handle(StartPlacementTestCommand request, CancellationToken cancellationToken)
    {
        var learners = await _learnerRepository.FindAsync(l => l.UserId == request.LearnerId);
        var learner = learners.FirstOrDefault();
        if (learner == null)
        {
            return ApiResponse<List<PlacementQuestionDto>>.FailureResponse("Hồ sơ học viên không tồn tại hoặc dữ liệu tài khoản bị lệch pha.");
        }

        // 1. Check if user already took the placement test
        var placementQuiz = (await _quizRepository.FindAsync(q => q.Level == EnglishLevel.PlacementTest)).FirstOrDefault();
        
        if (placementQuiz != null)
        {
            var existingAttempts = await _attemptRepository.FindAsync(a => a.QuizId == placementQuiz.Id && a.LearnerProfileId == learner.Id);
            if (existingAttempts.Any())
            {
                return ApiResponse<List<PlacementQuestionDto>>.FailureResponse("Tài khoản của bạn đã thực hiện bài kiểm tra đánh giá năng lực đầu vào này rồi!");
            }
        }
        
        var existingResults = await _testResultRepository.FindAsync(r => r.LearnerProfileId == learner.Id);
        if (existingResults.Any())
        {
            return ApiResponse<List<PlacementQuestionDto>>.FailureResponse("Tài khoản của bạn đã thực hiện bài kiểm tra đánh giá năng lực đầu vào này rồi!");
        }

        if (placementQuiz == null)
        {
            return ApiResponse<List<PlacementQuestionDto>>.FailureResponse("Hệ thống chưa có sẵn bài kiểm tra đầu vào. Vui lòng quay lại sau.");
        }

        // 2. Fetch questions and options
        var questions = await _questionRepository.FindAsync(q => q.QuizId == placementQuiz.Id);
        var questionIds = questions.Select(q => q.Id).ToList();
        var allOptions = await _answerOptionRepository.FindAsync(o => questionIds.Contains(o.QuestionId));
        var optionsByQuestion = allOptions
            .GroupBy(o => o.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Id).Select(o => o.OptionText).ToList());

        var questionDtos = questions.Select(q => new PlacementQuestionDto(
            q.Id,
            q.Content,
            optionsByQuestion.TryGetValue(q.Id, out var opts) ? opts : new List<string>()
        )).ToList();

        return ApiResponse<List<PlacementQuestionDto>>.SuccessResponse(questionDtos, "Placement test questions retrieved successfully.");
    }
}

// SUBMIT TEST
public record SubmitPlacementTestCommand(int LearnerId, List<PlacementAnswerInput> Answers) : IRequest<ApiResponse<PlacementSubmitResponse>>;

public class SubmitPlacementTestCommandHandler : IRequestHandler<SubmitPlacementTestCommand, ApiResponse<PlacementSubmitResponse>>
{
    private readonly IRepository<LearnerProfile> _learnerRepository;
    private readonly IRepository<PlacementTestResult> _testResultRepository;
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<Question> _questionRepository;
    private readonly IRepository<AnswerOption> _answerOptionRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;
    private readonly IKafkaPublisher _kafkaPublisher;

    public SubmitPlacementTestCommandHandler(
        IRepository<LearnerProfile> learnerRepository, 
        IRepository<PlacementTestResult> testResultRepository,
        IRepository<Quiz> quizRepository,
        IRepository<Question> questionRepository,
        IRepository<AnswerOption> answerOptionRepository,
        IRepository<QuizAttempt> attemptRepository,
        IKafkaPublisher kafkaPublisher)
    {
        _learnerRepository = learnerRepository;
        _testResultRepository = testResultRepository;
        _quizRepository = quizRepository;
        _questionRepository = questionRepository;
        _answerOptionRepository = answerOptionRepository;
        _attemptRepository = attemptRepository;
        _kafkaPublisher = kafkaPublisher;
    }

    public async Task<ApiResponse<PlacementSubmitResponse>> Handle(SubmitPlacementTestCommand request, CancellationToken cancellationToken)
    {
        var learners = await _learnerRepository.FindAsync(l => l.UserId == request.LearnerId);
        var learner = learners.FirstOrDefault();
        if (learner == null) return ApiResponse<PlacementSubmitResponse>.FailureResponse("Hồ sơ học viên không tồn tại hoặc dữ liệu tài khoản bị lệch pha.");

        // 1. Check if user already took the placement test
        var placementQuiz = (await _quizRepository.FindAsync(q => q.Level == EnglishLevel.PlacementTest)).FirstOrDefault();

        if (placementQuiz != null)
        {
            var existingAttempts = await _attemptRepository.FindAsync(a => a.QuizId == placementQuiz.Id && a.LearnerProfileId == learner.Id);
            if (existingAttempts.Any())
            {
                return ApiResponse<PlacementSubmitResponse>.FailureResponse("Tài khoản của bạn đã thực hiện bài kiểm tra đánh giá năng lực đầu vào này rồi!");
            }
        }

        var existingResults = await _testResultRepository.FindAsync(r => r.LearnerProfileId == learner.Id);
        if (existingResults.Any())
        {
            return ApiResponse<PlacementSubmitResponse>.FailureResponse("Tài khoản của bạn đã thực hiện bài kiểm tra đánh giá năng lực đầu vào này rồi!");
        }

        if (placementQuiz == null)
        {
            return ApiResponse<PlacementSubmitResponse>.FailureResponse("Hệ thống chưa có sẵn bài kiểm tra đầu vào. Vui lòng quay lại sau.");
        }

        // 2. Fetch questions and options
        var questions = (await _questionRepository.FindAsync(q => q.QuizId == placementQuiz.Id)).ToList();
        var questionIds = questions.Select(q => q.Id).ToList();
        var allOptions = await _answerOptionRepository.FindAsync(o => questionIds.Contains(o.QuestionId));
        var optionsByQuestion = allOptions
            .GroupBy(o => o.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Id).ToList());

        int correctCount = 0;
        int incorrectCount = 0;
        var attemptDetails = new List<QuizAttemptDetail>();

        foreach (var q in questions)
        {
            optionsByQuestion.TryGetValue(q.Id, out var options);
            options ??= new List<AnswerOption>();

            var userAnswer = request.Answers.FirstOrDefault(a => a.QuestionId == q.Id);
            bool isCorrect = false;
            int? selectedOptionId = null;

            if (userAnswer != null)
            {
                var correctOption = options.FirstOrDefault(o => o.IsCorrect);
                AnswerOption? selectedOption = null;

                if (userAnswer.SelectedOptionIndex >= 0 && userAnswer.SelectedOptionIndex < options.Count)
                {
                    selectedOption = options[userAnswer.SelectedOptionIndex];
                }

                if (selectedOption != null)
                {
                    selectedOptionId = selectedOption.Id;
                    isCorrect = correctOption != null && selectedOption.Id == correctOption.Id;
                }
            }

            if (isCorrect) correctCount++;
            else incorrectCount++;

            attemptDetails.Add(new QuizAttemptDetail
            {
                QuestionId = q.Id,
                SelectedAnswerOptionId = selectedOptionId,
                IsCorrect = isCorrect
            });
        }

        double totalQuestions = questions.Count > 0 ? questions.Count : 1;
        double rawScore = ((double)correctCount / totalQuestions) * 10.0;
        int scoreOutOf10 = (int)Math.Round(rawScore);
        int scorePercentage = (int)Math.Round(rawScore * 10.0);

        EnglishLevel recommended = EnglishLevel.A1;
        if (scoreOutOf10 <= 5) recommended = EnglishLevel.A1;
        else if (scoreOutOf10 == 6) recommended = EnglishLevel.A2;
        else if (scoreOutOf10 == 7) recommended = EnglishLevel.B1;
        else if (scoreOutOf10 == 8) recommended = EnglishLevel.B2;
        else if (scoreOutOf10 == 9) recommended = EnglishLevel.C1;
        else recommended = EnglishLevel.C2;

        // Save to QuizAttempts table
        var attempt = new QuizAttempt
        {
            QuizId = placementQuiz.Id,
            LearnerProfileId = learner.Id,
            Score = scorePercentage, // Store out of 100 percentage for consistency in standard reports
            CorrectAnswersCount = correctCount,
            IncorrectAnswersCount = incorrectCount,
            IsPassed = rawScore >= (placementQuiz.PassingScore / 10.0),
            AttemptedAt = DateTime.UtcNow,
            Details = attemptDetails
        };
        await _attemptRepository.AddAsync(attempt);
        await _attemptRepository.SaveChangesAsync();

        // Save to PlacementTestResult table
        var result = new PlacementTestResult
        {
            LearnerProfileId = learner.Id,
            Score = scorePercentage,
            RecommendedLevel = recommended,
            TakenAt = DateTime.UtcNow
        };
        await _testResultRepository.AddAsync(result);
        await _testResultRepository.SaveChangesAsync();

        // Update learner profile level
        try
        {
            learner.Level = recommended;
            learner.LastActiveAt = DateTime.UtcNow;
            await _learnerRepository.UpdateAsync(learner);
            await _learnerRepository.SaveChangesAsync();
        }
        catch (Exception ex)
        {
            // Gracefully ignore profile sync failures to prevent crashing the response
            Console.WriteLine($"Error syncing learner level: {ex.Message}");
        }

        // Fire events
        var ev = new PlacementTestCompletedEvent(result.Id, learner.Id, scorePercentage, recommended, DateTime.UtcNow);
        await _kafkaPublisher.PublishPlacementTestCompletedAsync(ev);

        var response = new PlacementSubmitResponse(
            scorePercentage, 
            recommended.ToString(), 
            $"Kết quả đánh giá của bạn là: {recommended}. Chúc bạn học tập vui vẻ!"
        );
        return ApiResponse<PlacementSubmitResponse>.SuccessResponse(response, "Placement test submitted successfully.");
    }
}

// GET PLACEMENT STATUS QUERY
public record GetPlacementStatusQuery(int LearnerId) : IRequest<PlacementStatusResponseDto>;

public record PlacementStatusResponseDto(bool HasTaken, int? QuizId, string Message);

public class GetPlacementStatusQueryHandler : IRequestHandler<GetPlacementStatusQuery, PlacementStatusResponseDto>
{
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;
    private readonly IRepository<PlacementTestResult> _testResultRepository;
    private readonly IRepository<LearnerProfile> _learnerRepository;

    public GetPlacementStatusQueryHandler(
        IRepository<Quiz> quizRepository,
        IRepository<QuizAttempt> attemptRepository,
        IRepository<PlacementTestResult> testResultRepository,
        IRepository<LearnerProfile> learnerRepository)
    {
        _quizRepository = quizRepository;
        _attemptRepository = attemptRepository;
        _testResultRepository = testResultRepository;
        _learnerRepository = learnerRepository;
    }

    public async Task<PlacementStatusResponseDto> Handle(GetPlacementStatusQuery request, CancellationToken cancellationToken)
    {
        var placementQuiz = (await _quizRepository.FindAsync(q => q.Level == EnglishLevel.PlacementTest || q.IsPlacementTest)).FirstOrDefault();
        int? quizId = placementQuiz?.Id;

        var learners = await _learnerRepository.FindAsync(l => l.UserId == request.LearnerId);
        var learner = learners.FirstOrDefault();
        if (learner == null) return new PlacementStatusResponseDto(false, quizId, "Hồ sơ học viên không tồn tại.");

        bool hasAttempt = false;
        if (placementQuiz != null)
        {
            var attempts = await _attemptRepository.FindAsync(a => a.QuizId == placementQuiz.Id && a.LearnerProfileId == learner.Id);
            if (attempts.Any())
            {
                hasAttempt = true;
            }
        }

        if (!hasAttempt)
        {
            var results = await _testResultRepository.FindAsync(r => r.LearnerProfileId == learner.Id);
            if (results.Any())
            {
                hasAttempt = true;
            }
        }

        return new PlacementStatusResponseDto(hasAttempt, quizId, "Success");
    }
}
