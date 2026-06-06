using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Interfaces;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Application.Features.Quizzes;
using CoreLearningSystem.Application.Features.Grading;
using CoreLearningSystem.Application.Features.Placement;
using CoreLearningSystem.Application.Features.StudentAnswers;
using CoreLearningSystem.Domain.Enums;

namespace CoreLearningSystem.API.Controllers;

[Authorize]
public class QuizzesController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<QuizDto>>>> GetAll([FromQuery] EnglishLevel? level)
    {
        var result = await Mediator.Send(new GetQuizzesQuery(level));
        return Ok(result);
    }

    [HttpGet("placement")]
    public async Task<ActionResult<ApiResponse<QuizDto>>> GetPlacementQuiz()
    {
        var result = await Mediator.Send(new GetQuizzesQuery(EnglishLevel.PlacementTest));
        if (result.Success && result.Data != null)
        {
            var placementQuiz = result.Data.FirstOrDefault();
            if (placementQuiz != null)
            {
                return Ok(ApiResponse<QuizDto>.SuccessResponse(placementQuiz));
            }
        }
        return NotFound(ApiResponse<QuizDto>.FailureResponse("Không tìm thấy bài kiểm tra đầu vào."));
    }

    [HttpGet("placement/status")]
    public async Task<IActionResult> GetPlacementStatus()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(new { message = "Unauthorized. Please log in." });
        }

        var result = await Mediator.Send(new GetPlacementStatusQuery(userId));
        return Ok(new { 
            hasTaken = result.HasTaken, 
            quizId = result.QuizId, 
            message = result.Message 
        });
    }

    [HttpGet("placement/check")]
    public async Task<IActionResult> CheckPlacementStatus()
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(new { message = "Unauthorized. Please log in." });
        }

        var learnerRepository = HttpContext.RequestServices.GetRequiredService<IRepository<LearnerProfile>>();
        var quizRepository = HttpContext.RequestServices.GetRequiredService<IRepository<Quiz>>();
        var attemptRepository = HttpContext.RequestServices.GetRequiredService<IRepository<QuizAttempt>>();

        var profiles = await learnerRepository.FindAsync(l => l.UserId == userId);
        var profile = profiles.FirstOrDefault();
        if (profile == null) return NotFound(new { message = "Profile missing" });

        var quizzes = await quizRepository.FindAsync(q => q.IsPlacementTest || q.Level == EnglishLevel.PlacementTest);
        var placementQuizIds = quizzes.Select(q => q.Id).ToList();

        var attempts = await attemptRepository.FindAsync(a => a.LearnerProfileId == profile.Id && placementQuizIds.Contains(a.QuizId));
        bool hasTakenPlacement = attempts.Any();

        return Ok(new { 
            hasCompleted = hasTakenPlacement, 
            currentLevel = profile.Level.ToString() 
        });
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<ApiResponse<QuizDetailsDto>>> GetById(int id)
    {
        var result = await Mediator.Send(new GetQuizByIdQuery(id));
        if (!result.Success || result.Data == null)
        {
            return NotFound(ApiResponse<QuizDetailsDto>.FailureResponse(result.Message ?? "Không tìm thấy bài trắc nghiệm."));
        }
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<QuizDto>>> Create([FromBody] CreateQuizCommand command)
    {
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<QuizDto>>> Update(int id, [FromBody] UpdateQuizCommand command)
    {
        if (id != command.Id) return BadRequest(ApiResponse<QuizDto>.FailureResponse("Mismatched Quiz ID."));
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
    {
        var result = await Mediator.Send(new DeleteQuizCommand(id));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("{id}/attach-question/{questionId}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> AttachQuestion(int id, int questionId)
    {
        var result = await Mediator.Send(new AttachQuestionToQuizCommand(id, questionId));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("{id}/questions")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> BulkAddQuestions(int id, [FromBody] List<QuestionInputDto> questions)
    {
        var result = await Mediator.Send(new BulkAddQuestionsCommand(id, questions));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPost("{id}/submit")]
    public async Task<IActionResult> SubmitQuiz(int id, [FromBody] SubmitQuizDto dto)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out int userId))
        {
            return Unauthorized(new { message = "Unauthorized. Please log in." });
        }

        if (dto == null || id != dto.QuizId)
        {
            Console.WriteLine($"Validation fail: Route ID {id} does not match payload Quiz ID {dto?.QuizId}.");
            return BadRequest(new { message = "Dữ liệu câu hỏi gửi lên không trùng khớp với cấu trúc bài thi cấu hình trên hệ thống." });
        }

        var quizRepository = HttpContext.RequestServices.GetRequiredService<IRepository<Quiz>>();
        var quiz = await quizRepository.GetByIdAsync(id);
        if (quiz == null)
        {
            return BadRequest(new { message = $"Bài trắc nghiệm số {id} không tồn tại trên hệ thống." });
        }

        var result = await Mediator.Send(new SubmitQuizAttemptCommand(dto, userId));
        if (!result.Success || result.Data == null) return BadRequest(new { message = result.Message });

        bool isPlacement = quiz.IsPlacementTest || quiz.Level == EnglishLevel.PlacementTest;
        string successMessage = isPlacement 
            ? "Nộp bài đánh giá năng lực thành công!" 
            : "Nộp bài trắc nghiệm thành công!";

        return Ok(new {
            message = successMessage,
            score = result.Data.Score.ToString(),
            totalQuestions = dto.Answers.Count,
            correctAnswers = result.Data.CorrectCount,
            assignedLevel = result.Data.AssignedLevel,
            passed = result.Data.IsPassed
        });
    }

    [HttpPost("attempts/{attemptId}/answers")]
    public async Task<ActionResult<ApiResponse<bool>>> CreateStudentAnswers(int attemptId, [FromBody] List<CreateStudentAnswerInput> answers)
    {
        var result = await Mediator.Send(new CreateStudentAnswersCommand(attemptId, answers));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("attempts/{attemptId}/answers")]
    public async Task<ActionResult<ApiResponse<IEnumerable<StudentAnswerReviewDto>>>> GetAnswersByAttemptId(int attemptId)
    {
        var result = await Mediator.Send(new GetAnswersByAttemptIdQuery(attemptId));
        if (!result.Success) return NotFound(result);
        return Ok(result);
    }

    [HttpPut("attempts/{attemptId}/answers/{detailId}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> UpdateStudentAnswer(int attemptId, int detailId, [FromBody] UpdateStudentAnswerDto dto)
    {
        var result = await Mediator.Send(new UpdateStudentAnswerCommand(detailId, dto.SelectedAnswerOptionId));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("attempts/{attemptId}/answers")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> DeleteAnswersByAttemptId(int attemptId)
    {
        var result = await Mediator.Send(new DeleteAnswersByAttemptIdCommand(attemptId));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }
}

public record UpdateStudentAnswerDto(int SelectedAnswerOptionId);
