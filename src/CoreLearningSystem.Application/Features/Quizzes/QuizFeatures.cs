using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Application.Features.Quizzes;

public record QuizDto(int Id, string Title, string Description, int DurationMinutes, double PassingScore, double MaxScore, string Level);
public record QuizDetailsDto(int Id, string Title, string Description, int DurationMinutes, double PassingScore, double MaxScore, string Level, int XpReward, List<QuestionDto> Questions);
public record QuestionDto(int Id, string Content, string Text, string Skill, string Level, string Explanation, double Score, List<string> Options, int CorrectOptionIndex);
public record QuestionInputDto(string Content, SkillType Skill, string Topic, EnglishLevel Level, string CorrectAnswer, string Explanation, List<string> Options, int CorrectOptionIndex, double Score);

// READ ALL
public record GetQuizzesQuery(EnglishLevel? Level) : IRequest<ApiResponse<IEnumerable<QuizDto>>>;

public class GetQuizzesQueryHandler : IRequestHandler<GetQuizzesQuery, ApiResponse<IEnumerable<QuizDto>>>
{
    private readonly IRepository<Quiz> _quizRepository;

    public GetQuizzesQueryHandler(IRepository<Quiz> quizRepository)
    {
        _quizRepository = quizRepository;
    }

    public async Task<ApiResponse<IEnumerable<QuizDto>>> Handle(GetQuizzesQuery request, CancellationToken cancellationToken)
    {
        var quizzes = await _quizRepository.GetAllAsync();

        if (request.Level.HasValue)
        {
            quizzes = quizzes.Where(q => q.Level == request.Level.Value);
        }

        var dtos = quizzes.Select(q => new QuizDto(
            q.Id, 
            q.Title, 
            q.Description, 
            q.DurationMinutes, 
            q.PassingScore, 
            q.MaxScore,
            q.Level.ToString()
        ));
        return ApiResponse<IEnumerable<QuizDto>>.SuccessResponse(dtos);
    }
}

// READ BY ID
public record GetQuizByIdQuery(int Id) : IRequest<ApiResponse<QuizDetailsDto>>;

public class GetQuizByIdQueryHandler : IRequestHandler<GetQuizByIdQuery, ApiResponse<QuizDetailsDto>>
{
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<Question> _questionRepository;
    private readonly IRepository<AnswerOption> _answerOptionRepository;

    public GetQuizByIdQueryHandler(
        IRepository<Quiz> quizRepository,
        IRepository<Question> questionRepository,
        IRepository<AnswerOption> answerOptionRepository)
    {
        _quizRepository = quizRepository;
        _questionRepository = questionRepository;
        _answerOptionRepository = answerOptionRepository;
    }

    public async Task<ApiResponse<QuizDetailsDto>> Handle(GetQuizByIdQuery request, CancellationToken cancellationToken)
    {
        var quiz = await _quizRepository.GetByIdAsync(request.Id);
        if (quiz == null)
        {
            return ApiResponse<QuizDetailsDto>.FailureResponse("Không tìm thấy bài trắc nghiệm.");
        }

        var questions = await _questionRepository.FindAsync(q => q.QuizId == quiz.Id);
        var questionIds = questions.Select(q => q.Id).ToList();

        var options = await _answerOptionRepository.FindAsync(o => questionIds.Contains(o.QuestionId));
        var optionsByQuestion = options
            .GroupBy(o => o.QuestionId)
            .ToDictionary(g => g.Key, g => g.OrderBy(o => o.Id).ToList());

        var questionDtos = questions.Select(q =>
        {
            optionsByQuestion.TryGetValue(q.Id, out var opts);
            opts ??= new List<AnswerOption>();

            var correctIndex = opts.FindIndex(o => o.IsCorrect);

            return new QuestionDto(
                q.Id,
                q.Content,
                q.Content, // Map Content to Text to match FE QuizQuestion.text
                q.Skill.ToString(),
                q.Level.ToString(),
                q.Explanation,
                q.Score,
                opts.Select(o => o.OptionText).ToList(),
                correctIndex >= 0 ? correctIndex : 0
            );
        }).ToList();

        var details = new QuizDetailsDto(
            quiz.Id,
            quiz.Title,
            quiz.Description,
            quiz.DurationMinutes,
            quiz.PassingScore,
            quiz.MaxScore,
            quiz.Level.ToString(),
            100, // XP Reward
            questionDtos
        );

        return ApiResponse<QuizDetailsDto>.SuccessResponse(details);
    }
}

// CREATE
public record CreateQuizCommand(string Title, string Description, int DurationMinutes, double PassingScore, double MaxScore, EnglishLevel Level) : IRequest<ApiResponse<QuizDto>>;

public class CreateQuizCommandHandler : IRequestHandler<CreateQuizCommand, ApiResponse<QuizDto>>
{
    private readonly IRepository<Quiz> _quizRepository;

    public CreateQuizCommandHandler(IRepository<Quiz> quizRepository)
    {
        _quizRepository = quizRepository;
    }

    public async Task<ApiResponse<QuizDto>> Handle(CreateQuizCommand request, CancellationToken cancellationToken)
    {
        if (request.Level == EnglishLevel.PlacementTest)
        {
            var exists = await _quizRepository.FindAsync(q => q.Level == EnglishLevel.PlacementTest);
            if (exists.Any())
            {
                return ApiResponse<QuizDto>.FailureResponse("Hệ thống đã có sẵn một bài kiểm tra đầu vào. Bạn không thể tạo thêm bài mới trừ khi xóa hoặc đổi cấp độ của bài cũ.");
            }
        }

        var quiz = new Quiz
        {
            Title = request.Title,
            Description = request.Description,
            DurationMinutes = request.DurationMinutes,
            PassingScore = request.PassingScore,
            MaxScore = request.MaxScore,
            Level = request.Level,
            CreatedAt = DateTime.UtcNow
        };

        await _quizRepository.AddAsync(quiz);
        await _quizRepository.SaveChangesAsync();

        var dto = new QuizDto(quiz.Id, quiz.Title, quiz.Description, quiz.DurationMinutes, quiz.PassingScore, quiz.MaxScore, quiz.Level.ToString());
        return ApiResponse<QuizDto>.SuccessResponse(dto, "Quiz created successfully.");
    }
}

// UPDATE
public record UpdateQuizCommand(int Id, string Title, string Description, int DurationMinutes, double PassingScore, double MaxScore, EnglishLevel Level) : IRequest<ApiResponse<QuizDto>>;

public class UpdateQuizCommandHandler : IRequestHandler<UpdateQuizCommand, ApiResponse<QuizDto>>
{
    private readonly IRepository<Quiz> _quizRepository;

    public UpdateQuizCommandHandler(IRepository<Quiz> quizRepository)
    {
        _quizRepository = quizRepository;
    }

    public async Task<ApiResponse<QuizDto>> Handle(UpdateQuizCommand request, CancellationToken cancellationToken)
    {
        var quiz = await _quizRepository.GetByIdAsync(request.Id);
        if (quiz == null) return ApiResponse<QuizDto>.FailureResponse("Quiz not found.");

        if (request.Level == EnglishLevel.PlacementTest)
        {
            var exists = await _quizRepository.FindAsync(q => q.Level == EnglishLevel.PlacementTest && q.Id != request.Id);
            if (exists.Any())
            {
                return ApiResponse<QuizDto>.FailureResponse("Hệ thống đã có sẵn một bài kiểm tra đầu vào. Bạn không thể tạo thêm bài mới trừ khi xóa hoặc đổi cấp độ của bài cũ.");
            }
        }

        quiz.Title = request.Title;
        quiz.Description = request.Description;
        quiz.DurationMinutes = request.DurationMinutes;
        quiz.PassingScore = request.PassingScore;
        quiz.MaxScore = request.MaxScore;
        quiz.Level = request.Level;

        await _quizRepository.UpdateAsync(quiz);
        await _quizRepository.SaveChangesAsync();

        var dto = new QuizDto(quiz.Id, quiz.Title, quiz.Description, quiz.DurationMinutes, quiz.PassingScore, quiz.MaxScore, quiz.Level.ToString());
        return ApiResponse<QuizDto>.SuccessResponse(dto, "Quiz updated successfully.");
    }
}

// DELETE
public record DeleteQuizCommand(int Id) : IRequest<ApiResponse<bool>>;

public class DeleteQuizCommandHandler : IRequestHandler<DeleteQuizCommand, ApiResponse<bool>>
{
    private readonly IRepository<Quiz> _quizRepository;

    public DeleteQuizCommandHandler(IRepository<Quiz> quizRepository)
    {
        _quizRepository = quizRepository;
    }

    public async Task<ApiResponse<bool>> Handle(DeleteQuizCommand request, CancellationToken cancellationToken)
    {
        var quiz = await _quizRepository.GetByIdAsync(request.Id);
        if (quiz == null) return ApiResponse<bool>.FailureResponse("Quiz not found.");

        await _quizRepository.DeleteAsync(quiz);
        await _quizRepository.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Quiz deleted successfully.");
    }
}

// ATTACH QUESTION TO QUIZ
public record AttachQuestionToQuizCommand(int QuizId, int QuestionId) : IRequest<ApiResponse<bool>>;

public class AttachQuestionToQuizCommandHandler : IRequestHandler<AttachQuestionToQuizCommand, ApiResponse<bool>>
{
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<Question> _questionRepository;

    public AttachQuestionToQuizCommandHandler(IRepository<Quiz> quizRepository, IRepository<Question> questionRepository)
    {
        _quizRepository = quizRepository;
        _questionRepository = questionRepository;
    }

    public async Task<ApiResponse<bool>> Handle(AttachQuestionToQuizCommand request, CancellationToken cancellationToken)
    {
        var quiz = await _quizRepository.GetByIdAsync(request.QuizId);
        if (quiz == null) return ApiResponse<bool>.FailureResponse("Quiz not found.");

        var question = await _questionRepository.GetByIdAsync(request.QuestionId);
        if (question == null) return ApiResponse<bool>.FailureResponse("Question not found.");

        // Level validation: question level must match quiz level
        if (question.Level != quiz.Level)
        {
            return ApiResponse<bool>.FailureResponse("Cấp độ của câu hỏi không khớp với cấp độ của bộ đề.");
        }

        // Validation: Sum of scores must match MaxScore after attaching
        var existingQuestions = await _questionRepository.FindAsync(q => q.QuizId == request.QuizId);
        var currentSum = existingQuestions.Sum(q => q.Score) + question.Score;
        if (Math.Abs(currentSum - quiz.MaxScore) > 0.001)
        {
            return ApiResponse<bool>.FailureResponse($"Tổng điểm của các câu hỏi ({currentSum}) phải bằng chính xác điểm tối đa của bộ đề ({quiz.MaxScore}).");
        }

        question.QuizId = request.QuizId;
        await _questionRepository.UpdateAsync(question);
        await _questionRepository.SaveChangesAsync();

        return ApiResponse<bool>.SuccessResponse(true, "Question successfully attached to Quiz.");
    }
}

// BULK ADD QUESTIONS TO QUIZ PACKAGE with strict score validation
public record BulkAddQuestionsCommand(int QuizId, List<QuestionInputDto> Questions) : IRequest<ApiResponse<bool>>;

public class BulkAddQuestionsCommandHandler : IRequestHandler<BulkAddQuestionsCommand, ApiResponse<bool>>
{
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<Question> _questionRepository;

    public BulkAddQuestionsCommandHandler(IRepository<Quiz> quizRepository, IRepository<Question> questionRepository)
    {
        _quizRepository = quizRepository;
        _questionRepository = questionRepository;
    }

    public async Task<ApiResponse<bool>> Handle(BulkAddQuestionsCommand request, CancellationToken cancellationToken)
    {
        var quiz = await _quizRepository.GetByIdAsync(request.QuizId);
        if (quiz == null) return ApiResponse<bool>.FailureResponse("Quiz package not found.");

        // Level validation: check each question level matches quiz level
        foreach (var q in request.Questions)
        {
            if (q.Level != quiz.Level)
            {
                return ApiResponse<bool>.FailureResponse("Cấp độ của câu hỏi không khớp với cấp độ của bộ đề.");
            }
        }

        // Strict Score Validation (Existing + New sum == MaxScore)
        var existingQuestions = await _questionRepository.FindAsync(q => q.QuizId == request.QuizId);
        var currentSum = existingQuestions.Sum(q => q.Score) + request.Questions.Sum(q => q.Score);

        if (Math.Abs(currentSum - quiz.MaxScore) > 0.001)
        {
            return ApiResponse<bool>.FailureResponse($"Tổng điểm của các câu hỏi ({currentSum}) phải bằng chính xác điểm tối đa của bộ đề ({quiz.MaxScore}).");
        }

        foreach (var q in request.Questions)
        {
            var question = new Question
            {
                QuizId = request.QuizId,
                Content = q.Content,
                Skill = q.Skill,
                Topic = q.Topic,
                Level = q.Level,
                CorrectAnswer = q.CorrectAnswer,
                Explanation = q.Explanation,
                Score = q.Score
            };

            for (int i = 0; i < q.Options.Count; i++)
            {
                question.AnswerOptions.Add(new AnswerOption
                {
                    OptionText = q.Options[i],
                    IsCorrect = (i == q.CorrectOptionIndex)
                });
            }

            await _questionRepository.AddAsync(question);
        }

        await _questionRepository.SaveChangesAsync();
        return ApiResponse<bool>.SuccessResponse(true, "Questions successfully added to Quiz package in bulk.");
    }
}
