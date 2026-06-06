using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using CoreLearningSystem.Application.DTOs.Common;
using CoreLearningSystem.Application.Features.Lessons;
using CoreLearningSystem.Domain.Enums;
using CoreLearningSystem.Domain.Entities;
using CoreLearningSystem.Infrastructure.Persistence;

namespace CoreLearningSystem.API.Controllers;

[Authorize]
public class LessonsController : ApiControllerBase
{
    [HttpGet]
    public async Task<ActionResult<ApiResponse<IEnumerable<LessonDto>>>> Get(
        [FromQuery] SkillType? skill, 
        [FromQuery] string? topic, 
        [FromQuery] EnglishLevel? level, 
        [FromQuery] string? searchTerm)
    {
        int? userId = null;
        string? userRole = null;

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var parsedId))
        {
            userId = parsedId;
        }

        var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role);
        if (roleClaim != null)
        {
            userRole = roleClaim.Value;
        }

        var result = await Mediator.Send(new GetLessonsQuery(skill, topic, level, searchTerm, userId, userRole));
        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<LessonDto>>> Create([FromBody] CreateLessonCommand command)
    {
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpPut("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<LessonDto>>> Update(int id, [FromBody] UpdateLessonCommand command)
    {
        if (id != command.Id) return BadRequest(ApiResponse<LessonDto>.FailureResponse("Mismatched Lesson ID."));
        var result = await Mediator.Send(command);
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ApiResponse<bool>>> Delete(int id)
    {
        var result = await Mediator.Send(new DeleteLessonCommand(id));
        if (!result.Success) return BadRequest(result);
        return Ok(result);
    }

    [HttpGet("{id:int}")]
    public async Task<IActionResult> GetById([FromRoute] int id)
    {
        Console.WriteLine($"[ROUTING FIXED] Intercepted Lesson ID from route: {id}");
        int? userId = null;
        string? userRole = null;

        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim != null && int.TryParse(userIdClaim.Value, out var parsedId))
        {
            userId = parsedId;
        }

        var roleClaim = User.FindFirst(System.Security.Claims.ClaimTypes.Role);
        if (roleClaim != null)
        {
            userRole = roleClaim.Value;
        }

        var result = await Mediator.Send(new GetLessonByIdQuery(id, userId, userRole));
        if (!result.Success)
        {
            return NotFound(new { message = result.Message });
        }

        var dbContext = HttpContext.RequestServices.GetRequiredService<AppDbContext>();

        bool isCompleted = false;
        if (userId.HasValue)
        {
            var profile = await dbContext.LearnerProfiles.FirstOrDefaultAsync(p => p.UserId == userId.Value);
            if (profile != null)
            {
                isCompleted = await dbContext.LearnerProgresses.AnyAsync(p => p.LearnerProfileId == profile.Id && p.LessonId == id && p.IsCompleted);
            }
        }

        var dto = result.Data!;
        return Ok(new {
            id = dto.Id,
            title = dto.Title,
            content = dto.Content,
            skillType = dto.Skill,
            topic = dto.Topic,
            level = dto.Level,
            durationMinutes = dto.DurationInMinutes,
            status = dto.Status,
            linkedQuizId = dto.QuizId,
            linkedQuizTitle = dto.QuizTitle,
            linkedQuizTimeLimitMinutes = dto.QuizDurationMinutes,
            linkedQuizMaxScore = dto.QuizMaxScore,
            isCompleted = isCompleted
        });
    }

    [HttpPost("{id}/complete")]
    public async Task<IActionResult> CompleteLesson([FromRoute] int id)
    {
        var userIdClaim = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier);
        if (userIdClaim == null || !int.TryParse(userIdClaim.Value, out var userId))
        {
            return Unauthorized(new { success = false, message = "Unauthorized access." });
        }

        var dbContext = HttpContext.RequestServices.GetRequiredService<AppDbContext>();

        var profile = await dbContext.LearnerProfiles.FirstOrDefaultAsync(l => l.UserId == userId);
        if (profile == null)
        {
            return BadRequest(new { success = false, message = "Learner profile not found." });
        }

        var progress = await dbContext.LearnerProgresses
            .FirstOrDefaultAsync(p => p.LearnerProfileId == profile.Id && p.LessonId == id);

        if (progress == null)
        {
            progress = new LearnerProgress
            {
                LearnerProfileId = profile.Id,
                LessonId = id,
                IsCompleted = true,
                CompletedAt = DateTime.UtcNow,
                LastAccessedAt = DateTime.UtcNow
            };
            dbContext.LearnerProgresses.Add(progress);
        }
        else
        {
            progress.IsCompleted = true;
            progress.CompletedAt = DateTime.UtcNow;
            progress.LastAccessedAt = DateTime.UtcNow;
            dbContext.LearnerProgresses.Update(progress);
        }

        await dbContext.SaveChangesAsync();

        // Academic blueprint check for level promotion
        await CheckAndPromoteUserLevelAsync(dbContext, profile, HttpContext.RequestAborted);

        return Ok(new { success = true, message = "Trạng thái tiến độ bài học đã được lưu vĩnh viễn vào MySQL!" });
    }

    private async Task CheckAndPromoteUserLevelAsync(AppDbContext dbContext, LearnerProfile learner, System.Threading.CancellationToken cancellationToken)
    {
        var currentLevel = learner.Level;
        if (currentLevel == EnglishLevel.PlacementTest || currentLevel == EnglishLevel.None) return;

        var lessonsInTier = await dbContext.Lessons
            .Where(l => l.Level == currentLevel && l.Status == LessonStatus.Published)
            .ToListAsync(cancellationToken);

        if (lessonsInTier.Count == 0) return;

        foreach (var lesson in lessonsInTier)
        {
            var progress = await dbContext.LearnerProgresses
                .FirstOrDefaultAsync(p => p.LearnerProfileId == learner.Id && p.LessonId == lesson.Id && p.IsCompleted, cancellationToken);
            
            if (progress == null) return;

            if (lesson.QuizId.HasValue)
            {
                var completedAt = progress.CompletedAt ?? DateTime.MinValue;
                var isQuizPassed = await dbContext.QuizAttempts
                    .AnyAsync(a => a.LearnerProfileId == learner.Id 
                                   && a.QuizId == lesson.QuizId.Value 
                                   && (a.Score >= 50.0 || a.IsPassed)
                                   && a.AttemptedAt >= completedAt.AddSeconds(-10), cancellationToken);

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
            dbContext.LearnerProfiles.Update(learner);
            await dbContext.SaveChangesAsync(cancellationToken);
            Console.WriteLine($"[ACADEMIC BLUEPRINT LEVEL UP] Learner {learner.Id} promoted from {currentLevel} to {nextLevel}!");
        }
    }
}
