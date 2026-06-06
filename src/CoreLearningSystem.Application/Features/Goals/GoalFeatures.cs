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

namespace CoreLearningSystem.Application.Features.Goals;

public record GoalDto(int Id, int LearnerId, string Target, string Type, double ProgressPercentage, bool IsCompleted, DateTime Deadline);

// READ ALL
public record GetGoalsQuery(int LearnerId) : IRequest<ApiResponse<IEnumerable<GoalDto>>>;

public class GetGoalsQueryHandler : IRequestHandler<GetGoalsQuery, ApiResponse<IEnumerable<GoalDto>>>
{
    private readonly IRepository<GoalSetting> _goalRepository;

    public GetGoalsQueryHandler(IRepository<GoalSetting> goalRepository)
    {
        _goalRepository = goalRepository;
    }

    public async Task<ApiResponse<IEnumerable<GoalDto>>> Handle(GetGoalsQuery request, CancellationToken cancellationToken)
    {
        var goals = await _goalRepository.FindAsync(g => g.LearnerProfileId == request.LearnerId);
        var dtos = goals.Select(g => new GoalDto(g.Id, g.LearnerProfileId, g.Target, g.Type.ToString(), g.ProgressPercentage, g.IsCompleted, g.Deadline));
        return ApiResponse<IEnumerable<GoalDto>>.SuccessResponse(dtos);
    }
}

// CREATE
public record CreateGoalCommand(int LearnerId, string Target, GoalType Type, DateTime Deadline) : IRequest<ApiResponse<GoalDto>>;

public class CreateGoalCommandHandler : IRequestHandler<CreateGoalCommand, ApiResponse<GoalDto>>
{
    private readonly IRepository<GoalSetting> _goalRepository;

    public CreateGoalCommandHandler(IRepository<GoalSetting> goalRepository)
    {
        _goalRepository = goalRepository;
    }

    public async Task<ApiResponse<GoalDto>> Handle(CreateGoalCommand request, CancellationToken cancellationToken)
    {
        var goal = new GoalSetting
        {
            LearnerProfileId = request.LearnerId,
            Target = request.Target,
            Type = request.Type,
            ProgressPercentage = 0.0,
            IsCompleted = false,
            Deadline = request.Deadline,
            CreatedAt = DateTime.UtcNow
        };

        await _goalRepository.AddAsync(goal);
        await _goalRepository.SaveChangesAsync();

        var dto = new GoalDto(goal.Id, goal.LearnerProfileId, goal.Target, goal.Type.ToString(), goal.ProgressPercentage, goal.IsCompleted, goal.Deadline);
        return ApiResponse<GoalDto>.SuccessResponse(dto, "Goal created successfully.");
    }
}

// UPDATE PROGRESS (Triggers GoalCompletedEvent)
public record UpdateGoalProgressCommand(int GoalId, double ProgressPercentage) : IRequest<ApiResponse<GoalDto>>;

public class UpdateGoalProgressCommandHandler : IRequestHandler<UpdateGoalProgressCommand, ApiResponse<GoalDto>>
{
    private readonly IRepository<GoalSetting> _goalRepository;
    private readonly IKafkaPublisher _kafkaPublisher;

    public UpdateGoalProgressCommandHandler(IRepository<GoalSetting> goalRepository, IKafkaPublisher kafkaPublisher)
    {
        _goalRepository = goalRepository;
        _kafkaPublisher = kafkaPublisher;
    }

    public async Task<ApiResponse<GoalDto>> Handle(UpdateGoalProgressCommand request, CancellationToken cancellationToken)
    {
        var goal = await _goalRepository.GetByIdAsync(request.GoalId);
        if (goal == null) return ApiResponse<GoalDto>.FailureResponse("Goal not found.");

        goal.ProgressPercentage = request.ProgressPercentage;
        if (request.ProgressPercentage >= 100.0 && !goal.IsCompleted)
        {
            goal.IsCompleted = true;
            goal.ProgressPercentage = 100.0;

            // Trigger Kafka event
            var ev = new GoalCompletedEvent(goal.Id, goal.LearnerProfileId, goal.Target, DateTime.UtcNow);
            await _kafkaPublisher.PublishGoalCompletedAsync(ev);
        }

        await _goalRepository.UpdateAsync(goal);
        await _goalRepository.SaveChangesAsync();

        var dto = new GoalDto(goal.Id, goal.LearnerProfileId, goal.Target, goal.Type.ToString(), goal.ProgressPercentage, goal.IsCompleted, goal.Deadline);
        return ApiResponse<GoalDto>.SuccessResponse(dto, "Goal progress updated successfully.");
    }
}
