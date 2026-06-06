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

namespace CoreLearningSystem.Application.Features.StudentAnswers;

// Inputs & DTOs
public record CreateStudentAnswerInput(int QuestionId, int SelectedAnswerOptionId);

public record StudentAnswerReviewDto(
    int DetailId,
    int QuestionId,
    string QuestionText,
    int? SelectedAnswerOptionId,
    string SelectedOptionText,
    int? CorrectAnswerOptionId,
    string CorrectOptionText,
    bool IsCorrect
);

// CREATE COMMAND
public record CreateStudentAnswersCommand(int AttemptId, List<CreateStudentAnswerInput> Answers) : IRequest<ApiResponse<bool>>;

public class CreateStudentAnswersCommandHandler : IRequestHandler<CreateStudentAnswersCommand, ApiResponse<bool>>
{
    private readonly IRepository<QuizAttempt> _attemptRepository;
    private readonly IRepository<QuizAttemptDetail> _detailRepository;
    private readonly IRepository<Question> _questionRepository;
    private readonly IRepository<AnswerOption> _optionRepository;
    private readonly IRepository<Quiz> _quizRepository;

    public CreateStudentAnswersCommandHandler(
        IRepository<QuizAttempt> attemptRepository,
        IRepository<QuizAttemptDetail> detailRepository,
        IRepository<Question> questionRepository,
        IRepository<AnswerOption> optionRepository,
        IRepository<Quiz> quizRepository)
    {
        _attemptRepository = attemptRepository;
        _detailRepository = detailRepository;
        _questionRepository = questionRepository;
        _optionRepository = optionRepository;
        _quizRepository = quizRepository;
    }

    public async Task<ApiResponse<bool>> Handle(CreateStudentAnswersCommand request, CancellationToken cancellationToken)
    {
        var attempt = await _attemptRepository.GetByIdAsync(request.AttemptId);
        if (attempt == null) return ApiResponse<bool>.FailureResponse("Quiz attempt not found.");

        var quiz = await _quizRepository.GetByIdAsync(attempt.QuizId);
        if (quiz == null) return ApiResponse<bool>.FailureResponse("Quiz not found.");

        await _attemptRepository.BeginTransactionAsync();
        try
        {
            // Clear existing details first to avoid duplicate selection constraint issues
            var existing = await _detailRepository.FindAsync(d => d.QuizAttemptId == request.AttemptId);
            foreach (var d in existing)
            {
                await _detailRepository.DeleteAsync(d);
            }
            await _detailRepository.SaveChangesAsync();

            // Bulk insert new student answers
            foreach (var item in request.Answers)
            {
                var options = await _optionRepository.FindAsync(o => o.QuestionId == item.QuestionId);
                var chosenOption = options.FirstOrDefault(o => o.Id == item.SelectedAnswerOptionId);
                bool isCorrect = chosenOption?.IsCorrect ?? false;

                var detail = new QuizAttemptDetail
                {
                    QuizAttemptId = request.AttemptId,
                    QuestionId = item.QuestionId,
                    SelectedAnswerOptionId = item.SelectedAnswerOptionId,
                    IsCorrect = isCorrect
                };
                await _detailRepository.AddAsync(detail);
            }
            await _detailRepository.SaveChangesAsync();

            // Calculate and update attempt totals
            var details = await _detailRepository.FindAsync(d => d.QuizAttemptId == request.AttemptId);
            int correctCount = details.Count(d => d.IsCorrect);
            
            var questions = await _questionRepository.FindAsync(q => q.QuizId == quiz.Id);
            int totalQuestionsCount = questions.Count();
            if (totalQuestionsCount == 0) totalQuestionsCount = 1;

            double score = ((double)correctCount / totalQuestionsCount) * 100.0;
            bool isPassed = score >= quiz.PassingScore;

            attempt.CorrectAnswersCount = correctCount;
            attempt.IncorrectAnswersCount = Math.Max(0, totalQuestionsCount - correctCount);
            attempt.Score = score;
            attempt.IsPassed = isPassed;

            await _attemptRepository.UpdateAsync(attempt);
            await _attemptRepository.SaveChangesAsync();

            await _attemptRepository.CommitTransactionAsync();
            return ApiResponse<bool>.SuccessResponse(true, "Student answers logged and attempt graded successfully.");
        }
        catch (Exception ex)
        {
            await _attemptRepository.RollbackTransactionAsync();
            return ApiResponse<bool>.FailureResponse($"Failed to save student answers: {ex.Message}");
        }
    }
}

// GET REVIEW HISTORY QUERY
public record GetAnswersByAttemptIdQuery(int AttemptId) : IRequest<ApiResponse<IEnumerable<StudentAnswerReviewDto>>>;

public class GetAnswersByAttemptIdQueryHandler : IRequestHandler<GetAnswersByAttemptIdQuery, ApiResponse<IEnumerable<StudentAnswerReviewDto>>>
{
    private readonly IRepository<QuizAttemptDetail> _detailRepository;
    private readonly IRepository<Question> _questionRepository;
    private readonly IRepository<AnswerOption> _optionRepository;

    public GetAnswersByAttemptIdQueryHandler(
        IRepository<QuizAttemptDetail> detailRepository,
        IRepository<Question> questionRepository,
        IRepository<AnswerOption> optionRepository)
    {
        _detailRepository = detailRepository;
        _questionRepository = questionRepository;
        _optionRepository = optionRepository;
    }

    public async Task<ApiResponse<IEnumerable<StudentAnswerReviewDto>>> Handle(GetAnswersByAttemptIdQuery request, CancellationToken cancellationToken)
    {
        var details = await _detailRepository.FindAsync(d => d.QuizAttemptId == request.AttemptId);
        var reviewList = new List<StudentAnswerReviewDto>();

        foreach (var detail in details)
        {
            var question = await _questionRepository.GetByIdAsync(detail.QuestionId);
            string questionText = question?.Content ?? "Unknown Question";

            var options = await _optionRepository.FindAsync(o => o.QuestionId == detail.QuestionId);
            var selectedOption = options.FirstOrDefault(o => o.Id == detail.SelectedAnswerOptionId);
            var correctOption = options.FirstOrDefault(o => o.IsCorrect);

            reviewList.Add(new StudentAnswerReviewDto(
                detail.Id,
                detail.QuestionId,
                questionText,
                detail.SelectedAnswerOptionId,
                selectedOption?.OptionText ?? "No selection",
                correctOption?.Id,
                correctOption?.OptionText ?? "No correct option defined",
                detail.IsCorrect
            ));
        }

        return ApiResponse<IEnumerable<StudentAnswerReviewDto>>.SuccessResponse(reviewList, "Review history retrieved successfully.");
    }
}

// UPDATE COMMAND (ADMIN OVERRIDE)
public record UpdateStudentAnswerCommand(int DetailId, int SelectedAnswerOptionId) : IRequest<ApiResponse<bool>>;

public class UpdateStudentAnswerCommandHandler : IRequestHandler<UpdateStudentAnswerCommand, ApiResponse<bool>>
{
    private readonly IRepository<QuizAttemptDetail> _detailRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;
    private readonly IRepository<AnswerOption> _optionRepository;
    private readonly IRepository<Quiz> _quizRepository;
    private readonly IRepository<Question> _questionRepository;

    public UpdateStudentAnswerCommandHandler(
        IRepository<QuizAttemptDetail> detailRepository,
        IRepository<QuizAttempt> attemptRepository,
        IRepository<AnswerOption> optionRepository,
        IRepository<Quiz> quizRepository,
        IRepository<Question> questionRepository)
    {
        _detailRepository = detailRepository;
        _attemptRepository = attemptRepository;
        _optionRepository = optionRepository;
        _quizRepository = quizRepository;
        _questionRepository = questionRepository;
    }

    public async Task<ApiResponse<bool>> Handle(UpdateStudentAnswerCommand request, CancellationToken cancellationToken)
    {
        var detail = await _detailRepository.GetByIdAsync(request.DetailId);
        if (detail == null) return ApiResponse<bool>.FailureResponse("Answer detail not found.");

        var option = await _optionRepository.GetByIdAsync(request.SelectedAnswerOptionId);
        if (option == null || option.QuestionId != detail.QuestionId)
        {
            return ApiResponse<bool>.FailureResponse("Invalid option selection for the associated question.");
        }

        // Update detail selection
        detail.SelectedAnswerOptionId = request.SelectedAnswerOptionId;
        detail.IsCorrect = option.IsCorrect;

        await _detailRepository.UpdateAsync(detail);
        await _detailRepository.SaveChangesAsync();

        // Recalculate parent attempt score
        var attempt = await _attemptRepository.GetByIdAsync(detail.QuizAttemptId);
        if (attempt != null)
        {
            var quiz = await _quizRepository.GetByIdAsync(attempt.QuizId);
            if (quiz != null)
            {
                var details = await _detailRepository.FindAsync(d => d.QuizAttemptId == attempt.Id);
                int correctCount = details.Count(d => d.IsCorrect);
                
                var questions = await _questionRepository.FindAsync(q => q.QuizId == quiz.Id);
                int totalQuestionsCount = questions.Count();
                if (totalQuestionsCount == 0) totalQuestionsCount = 1;

                double score = ((double)correctCount / totalQuestionsCount) * 100.0;
                bool isPassed = score >= quiz.PassingScore;

                attempt.CorrectAnswersCount = correctCount;
                attempt.IncorrectAnswersCount = Math.Max(0, totalQuestionsCount - correctCount);
                attempt.Score = score;
                attempt.IsPassed = isPassed;

                await _attemptRepository.UpdateAsync(attempt);
                await _attemptRepository.SaveChangesAsync();
            }
        }

        return ApiResponse<bool>.SuccessResponse(true, "Student answer updated and quiz attempt score recalculated successfully.");
    }
}

// DELETE COMMAND
public record DeleteAnswersByAttemptIdCommand(int AttemptId) : IRequest<ApiResponse<bool>>;

public class DeleteAnswersByAttemptIdCommandHandler : IRequestHandler<DeleteAnswersByAttemptIdCommand, ApiResponse<bool>>
{
    private readonly IRepository<QuizAttemptDetail> _detailRepository;
    private readonly IRepository<QuizAttempt> _attemptRepository;

    public DeleteAnswersByAttemptIdCommandHandler(
        IRepository<QuizAttemptDetail> detailRepository,
        IRepository<QuizAttempt> attemptRepository)
    {
        _detailRepository = detailRepository;
        _attemptRepository = attemptRepository;
    }

    public async Task<ApiResponse<bool>> Handle(DeleteAnswersByAttemptIdCommand request, CancellationToken cancellationToken)
    {
        var details = await _detailRepository.FindAsync(d => d.QuizAttemptId == request.AttemptId);
        foreach (var detail in details)
        {
            await _detailRepository.DeleteAsync(detail);
        }
        await _detailRepository.SaveChangesAsync();

        var attempt = await _attemptRepository.GetByIdAsync(request.AttemptId);
        if (attempt != null)
        {
            attempt.CorrectAnswersCount = 0;
            attempt.IncorrectAnswersCount = 0;
            attempt.Score = 0.0;
            attempt.IsPassed = false;

            await _attemptRepository.UpdateAsync(attempt);
            await _attemptRepository.SaveChangesAsync();
        }

        return ApiResponse<bool>.SuccessResponse(true, "All answers for the specified quiz attempt have been deleted.");
    }
}
