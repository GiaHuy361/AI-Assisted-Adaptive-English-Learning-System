using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using MediatR;
using FluentValidation;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Application.Interfaces;

namespace CoreLearningSystem.Application.Features.Questions;

public record AnswerOptionDto(int Id, string OptionText, bool IsCorrect);
public record QuestionDetailDto(int Id, int QuizId, string Content, string? ReadingText, string Skill, string Topic, string Level, string CorrectAnswer, string Explanation, List<AnswerOptionDto> Options, double Score);

// READ ALL
public record GetQuestionsQuery() : IRequest<ApiResponse<IEnumerable<QuestionDetailDto>>>;

public class GetQuestionsQueryHandler : IRequestHandler<GetQuestionsQuery, ApiResponse<IEnumerable<QuestionDetailDto>>>
{
    private readonly IRepository<Question> _questionRepository;
    private readonly IRepository<AnswerOption> _optionRepository;

    public GetQuestionsQueryHandler(IRepository<Question> questionRepository, IRepository<AnswerOption> optionRepository)
    {
        _questionRepository = questionRepository;
        _optionRepository = optionRepository;
    }

    public async Task<ApiResponse<IEnumerable<QuestionDetailDto>>> Handle(GetQuestionsQuery request, CancellationToken cancellationToken)
    {
        var questions = await _questionRepository.GetAllAsync();
        var options = await _optionRepository.GetAllAsync();
        var optionGroup = options.GroupBy(o => o.QuestionId).ToDictionary(g => g.Key, g => g.ToList());

        var dtos = questions.Select(q => new QuestionDetailDto(
            q.Id,
            q.QuizId,
            q.Content,
            q.ReadingText,
            q.Skill.ToString(),
            q.Topic,
            q.Level.ToString(),
            q.CorrectAnswer,
            q.Explanation,
            optionGroup.TryGetValue(q.Id, out var opts) 
                ? opts.Select(o => new AnswerOptionDto(o.Id, o.OptionText, o.IsCorrect)).ToList() 
                : new List<AnswerOptionDto>(),
            q.Score
        ));

        return ApiResponse<IEnumerable<QuestionDetailDto>>.SuccessResponse(dtos);
    }
}

// CREATE
public record CreateQuestionCommand(int QuizId, string Content, string? ReadingText, SkillType Skill, string Topic, EnglishLevel Level, string CorrectAnswer, string Explanation, List<string> Options, int CorrectOptionIndex, double Score) : IRequest<ApiResponse<QuestionDetailDto>>;

public class CreateQuestionCommandValidator : AbstractValidator<CreateQuestionCommand>
{
    public CreateQuestionCommandValidator()
    {
        RuleFor(x => x.Content).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.CorrectAnswer).NotEmpty();
        RuleFor(x => x.Options).NotEmpty();
        RuleFor(x => x.Score).GreaterThan(0);
    }
}

public class CreateQuestionCommandHandler : IRequestHandler<CreateQuestionCommand, ApiResponse<QuestionDetailDto>>
{
    private readonly IRepository<Question> _questionRepository;
    private readonly IRepository<Quiz> _quizRepository;
    private readonly ISignalRService _signalRService;

    public CreateQuestionCommandHandler(IRepository<Question> questionRepository, IRepository<Quiz> quizRepository, ISignalRService signalRService)
    {
        _questionRepository = questionRepository;
        _quizRepository = quizRepository;
        _signalRService = signalRService;
    }

    public async Task<ApiResponse<QuestionDetailDto>> Handle(CreateQuestionCommand request, CancellationToken cancellationToken)
    {
        var quiz = await _quizRepository.GetByIdAsync(request.QuizId);
        if (quiz == null) return ApiResponse<QuestionDetailDto>.FailureResponse("Quiz not found.");

        if (request.Level != quiz.Level)
        {
            return ApiResponse<QuestionDetailDto>.FailureResponse("Cấp độ của câu hỏi không khớp với cấp độ của bộ đề.");
        }

        var existingQuestions = await _questionRepository.FindAsync(q => q.QuizId == request.QuizId);
        var currentSum = existingQuestions.Sum(q => q.Score) + request.Score;
        if (Math.Abs(currentSum - quiz.MaxScore) > 0.001)
        {
            return ApiResponse<QuestionDetailDto>.FailureResponse($"Tổng điểm của các câu hỏi ({currentSum}) phải bằng chính xác điểm tối đa của bộ đề ({quiz.MaxScore}).");
        }

        var question = new Question
        {
            QuizId = request.QuizId,
            Content = request.Content,
            ReadingText = request.ReadingText,
            Skill = request.Skill,
            Topic = request.Topic,
            Level = request.Level,
            CorrectAnswer = request.CorrectAnswer,
            Explanation = request.Explanation,
            Score = request.Score
        };

        for (int i = 0; i < request.Options.Count; i++)
        {
            question.AnswerOptions.Add(new AnswerOption
            {
                OptionText = request.Options[i],
                IsCorrect = (i == request.CorrectOptionIndex)
            });
        }

        await _questionRepository.AddAsync(question);
        await _questionRepository.SaveChangesAsync();

        var dto = new QuestionDetailDto(
            question.Id,
            question.QuizId,
            question.Content,
            question.ReadingText,
            question.Skill.ToString(),
            question.Topic,
            question.Level.ToString(),
            question.CorrectAnswer,
            question.Explanation,
            question.AnswerOptions.Select(o => new AnswerOptionDto(o.Id, o.OptionText, o.IsCorrect)).ToList(),
            question.Score
        );

        try
        {
            await _signalRService.SendCrudUpdateAsync("Question", "Create", dto);
        }
        catch (Exception) { }

        return ApiResponse<QuestionDetailDto>.SuccessResponse(dto, "Question created in Bank successfully.");
    }
}

// UPDATE
public record UpdateQuestionCommand(int Id, int QuizId, string Content, string? ReadingText, SkillType Skill, string Topic, EnglishLevel Level, string CorrectAnswer, string Explanation, List<string> Options, int CorrectOptionIndex, double Score) : IRequest<ApiResponse<QuestionDetailDto>>;

public class UpdateQuestionCommandValidator : AbstractValidator<UpdateQuestionCommand>
{
    public UpdateQuestionCommandValidator()
    {
        RuleFor(x => x.Id).NotEmpty();
        RuleFor(x => x.Content).NotEmpty().MaximumLength(1000);
        RuleFor(x => x.CorrectAnswer).NotEmpty();
        RuleFor(x => x.Options).NotEmpty();
        RuleFor(x => x.Score).GreaterThan(0);
    }
}

public class UpdateQuestionCommandHandler : IRequestHandler<UpdateQuestionCommand, ApiResponse<QuestionDetailDto>>
{
    private readonly IRepository<Question> _questionRepository;
    private readonly IRepository<AnswerOption> _optionRepository;
    private readonly IRepository<Quiz> _quizRepository;
    private readonly ISignalRService _signalRService;

    public UpdateQuestionCommandHandler(IRepository<Question> questionRepository, IRepository<AnswerOption> optionRepository, IRepository<Quiz> quizRepository, ISignalRService signalRService)
    {
        _questionRepository = questionRepository;
        _optionRepository = optionRepository;
        _quizRepository = quizRepository;
        _signalRService = signalRService;
    }

    public async Task<ApiResponse<QuestionDetailDto>> Handle(UpdateQuestionCommand request, CancellationToken cancellationToken)
    {
        var question = await _questionRepository.GetByIdAsync(request.Id);
        if (question == null) return ApiResponse<QuestionDetailDto>.FailureResponse("Question not found.");

        var quiz = await _quizRepository.GetByIdAsync(request.QuizId);
        if (quiz == null) return ApiResponse<QuestionDetailDto>.FailureResponse("Quiz not found.");

        if (request.Level != quiz.Level)
        {
            return ApiResponse<QuestionDetailDto>.FailureResponse("Cấp độ của câu hỏi không khớp với cấp độ của bộ đề.");
        }

        var existingQuestions = await _questionRepository.FindAsync(q => q.QuizId == request.QuizId);
        var currentSum = existingQuestions.Where(q => q.Id != request.Id).Sum(q => q.Score) + request.Score;
        if (Math.Abs(currentSum - quiz.MaxScore) > 0.001)
        {
            return ApiResponse<QuestionDetailDto>.FailureResponse($"Tổng điểm của các câu hỏi ({currentSum}) phải bằng chính xác điểm tối đa của bộ đề ({quiz.MaxScore}).");
        }

        question.QuizId = request.QuizId;
        question.Content = request.Content;
        question.ReadingText = request.ReadingText;
        question.Skill = request.Skill;
        question.Topic = request.Topic;
        question.Level = request.Level;
        question.CorrectAnswer = request.CorrectAnswer;
        question.Explanation = request.Explanation;
        question.Score = request.Score;

        // Fetch and remove old options manually
        var oldOptions = await _optionRepository.FindAsync(o => o.QuestionId == question.Id);
        foreach (var opt in oldOptions)
        {
            await _optionRepository.DeleteAsync(opt);
        }

        question.AnswerOptions.Clear();
        for (int i = 0; i < request.Options.Count; i++)
        {
            question.AnswerOptions.Add(new AnswerOption
            {
                OptionText = request.Options[i],
                IsCorrect = (i == request.CorrectOptionIndex)
            });
        }

        await _questionRepository.UpdateAsync(question);
        await _questionRepository.SaveChangesAsync();

        var dto = new QuestionDetailDto(
            question.Id,
            question.QuizId,
            question.Content,
            question.ReadingText,
            question.Skill.ToString(),
            question.Topic,
            question.Level.ToString(),
            question.CorrectAnswer,
            question.Explanation,
            question.AnswerOptions.Select(o => new AnswerOptionDto(o.Id, o.OptionText, o.IsCorrect)).ToList(),
            question.Score
        );

        try
        {
            await _signalRService.SendCrudUpdateAsync("Question", "Update", dto);
        }
        catch (Exception) { }

        return ApiResponse<QuestionDetailDto>.SuccessResponse(dto, "Question updated successfully.");
    }
}

// DELETE
public record DeleteQuestionCommand(int Id) : IRequest<ApiResponse<bool>>;

public class DeleteQuestionCommandHandler : IRequestHandler<DeleteQuestionCommand, ApiResponse<bool>>
{
    private readonly IRepository<Question> _questionRepository;
    private readonly IRepository<AnswerOption> _optionRepository;
    private readonly ISignalRService _signalRService;

    public DeleteQuestionCommandHandler(IRepository<Question> questionRepository, IRepository<AnswerOption> optionRepository, ISignalRService signalRService)
    {
        _questionRepository = questionRepository;
        _optionRepository = optionRepository;
        _signalRService = signalRService;
    }

    public async Task<ApiResponse<bool>> Handle(DeleteQuestionCommand request, CancellationToken cancellationToken)
    {
        var question = await _questionRepository.GetByIdAsync(request.Id);
        if (question == null) return ApiResponse<bool>.FailureResponse("Question not found.");

        // Delete associated answer options
        var options = await _optionRepository.FindAsync(o => o.QuestionId == question.Id);
        foreach (var opt in options)
        {
            await _optionRepository.DeleteAsync(opt);
        }

        await _questionRepository.DeleteAsync(question);
        await _questionRepository.SaveChangesAsync();

        try
        {
            await _signalRService.SendCrudUpdateAsync("Question", "Delete", new { Id = request.Id });
        }
        catch (Exception) { }

        return ApiResponse<bool>.SuccessResponse(true, "Question deleted successfully.");
    }
}
