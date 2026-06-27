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

namespace CoreLearningSystem.Application.Features.LearningPaths;

public record LearningPathItemDto(int Id, int LessonId, string LessonTitle, int SequenceOrder, string Status);
public record LearningPathDto(int PathId, int LearnerId, string Status, List<LearningPathItemDto> Items);

// READ
public record GetLearningPathQuery(int LearnerId) : IRequest<ApiResponse<LearningPathDto>>;

public class GetLearningPathQueryHandler : IRequestHandler<GetLearningPathQuery, ApiResponse<LearningPathDto>>
{
    private readonly IRepository<LearningPath> _pathRepository;
    private readonly IRepository<Lesson> _lessonRepository;
    private readonly IRepository<LearnerProfile> _learnerRepository;

    public GetLearningPathQueryHandler(
        IRepository<LearningPath> pathRepository, 
        IRepository<Lesson> lessonRepository,
        IRepository<LearnerProfile> learnerRepository)
    {
        _pathRepository = pathRepository;
        _lessonRepository = lessonRepository;
        _learnerRepository = learnerRepository;
    }

    public async Task<ApiResponse<LearningPathDto>> Handle(GetLearningPathQuery request, CancellationToken cancellationToken)
    {
        // 1. Resolve LearnerProfile by LearnerId (prioritizing Profile Id, then falling back to UserId)
        var learners = await _learnerRepository.FindAsync(l => l.Id == request.LearnerId);
        var learnerProfile = learners.FirstOrDefault();
        if (learnerProfile == null)
        {
            learners = await _learnerRepository.FindAsync(l => l.UserId == request.LearnerId);
            learnerProfile = learners.FirstOrDefault();
        }

        if (learnerProfile == null)
        {
            return ApiResponse<LearningPathDto>.FailureResponse("No active Learner Profile found for this learner.");
        }

        // 2. Resolve or Create LearningPath
        var paths = await _pathRepository.FindAsync(p => p.LearnerId == learnerProfile.Id);
        var path = paths.FirstOrDefault();
        if (path == null)
        {
            path = new LearningPath
            {
                LearnerId = learnerProfile.Id,
                Status = LearningPathStatus.InProgress,
                CreatedAt = DateTime.UtcNow
            };
            await _pathRepository.AddAsync(path);
            await _pathRepository.SaveChangesAsync();
        }

        // 3. Fetch all published lessons
        var lessons = await _lessonRepository.FindAsync(l => l.Status == LessonStatus.Published);

        // 4. Construct adaptive pathway chain: starting exactly from current assigned level directly up to C2
        var activeLessons = lessons
            .Where(l => l.Level >= learnerProfile.Level && l.Level <= EnglishLevel.C2)
            .OrderBy(l => (int)l.Level)
            .ThenBy(l => l.Id)
            .ToList();

        // 5. Construct dynamically mapped DTO items
        var itemDtos = new List<LearningPathItemDto>();
        int sequenceOrder = 1;

        foreach (var lesson in activeLessons)
        {
            // Mark current level lessons as InProgress, future level lessons as Locked
            string status = lesson.Level == learnerProfile.Level ? "InProgress" : "Locked";
            
            itemDtos.Add(new LearningPathItemDto(
                0, // Dynamic item placeholder ID
                lesson.Id,
                lesson.Title,
                sequenceOrder++,
                status
            ));
        }

        var dto = new LearningPathDto(path.PathId, path.LearnerId, path.Status.ToString(), itemDtos);
        return ApiResponse<LearningPathDto>.SuccessResponse(dto);
    }
}

// CREATE OR ASSIGN PATH
public record CreateLearningPathCommand(int LearnerId, List<int> LessonIds) : IRequest<ApiResponse<LearningPathDto>>;

public class CreateLearningPathCommandHandler : IRequestHandler<CreateLearningPathCommand, ApiResponse<LearningPathDto>>
{
    private readonly IRepository<LearningPath> _pathRepository;
    private readonly IRepository<LearningPathItem> _itemRepository;

    public CreateLearningPathCommandHandler(IRepository<LearningPath> pathRepository, IRepository<LearningPathItem> itemRepository)
    {
        _pathRepository = pathRepository;
        _itemRepository = itemRepository;
    }

    public async Task<ApiResponse<LearningPathDto>> Handle(CreateLearningPathCommand request, CancellationToken cancellationToken)
    {
        // Delete existing path if any
        var existing = await _pathRepository.FindAsync(p => p.LearnerId == request.LearnerId);
        foreach (var p in existing)
        {
            await _pathRepository.DeleteAsync(p);
        }

        var newPath = new LearningPath
        {
            LearnerId = request.LearnerId,
            Status = LearningPathStatus.InProgress,
            CreatedAt = DateTime.UtcNow
        };

        await _pathRepository.AddAsync(newPath);
        await _pathRepository.SaveChangesAsync();

        int seq = 1;
        var items = new List<LearningPathItem>();
        foreach (var lid in request.LessonIds)
        {
            var item = new LearningPathItem
            {
                LearningPathId = newPath.PathId,
                LessonId = lid,
                SequenceOrder = seq++,
                Status = LessonStatus.Published
            };
            await _itemRepository.AddAsync(item);
            items.Add(item);
        }

        await _itemRepository.SaveChangesAsync();

        var itemDtos = items.Select(i => new LearningPathItemDto(i.Id, i.LessonId, "Lesson Assigned", i.SequenceOrder, i.Status.ToString())).ToList();
        var dto = new LearningPathDto(newPath.PathId, newPath.LearnerId, newPath.Status.ToString(), itemDtos);

        return ApiResponse<LearningPathDto>.SuccessResponse(dto, "Personalized learning path generated successfully.");
    }
}

// DYNAMIC ADAPTIVE ROADMAP STEPS BY LEVEL
public record PathStepDto(int Id, string Title, string Desc, string Status, int XpReward);

public record GetCurrentLearningPathQuery(int UserId) : IRequest<ApiResponse<IEnumerable<PathStepDto>>>;

public class GetCurrentLearningPathQueryHandler : IRequestHandler<GetCurrentLearningPathQuery, ApiResponse<IEnumerable<PathStepDto>>>
{
    private readonly IRepository<LearnerProfile> _profileRepository;

    public GetCurrentLearningPathQueryHandler(IRepository<LearnerProfile> profileRepository)
    {
        _profileRepository = profileRepository;
    }

    public async Task<ApiResponse<IEnumerable<PathStepDto>>> Handle(GetCurrentLearningPathQuery request, CancellationToken cancellationToken)
    {
        var profiles = await _profileRepository.FindAsync(p => p.UserId == request.UserId);
        var profile = profiles.FirstOrDefault();
        if (profile == null)
        {
            return ApiResponse<IEnumerable<PathStepDto>>.FailureResponse("No active Learner Profile found for this user.");
        }

        var currentLevel = profile.Level;
        if (currentLevel == EnglishLevel.PlacementTest)
        {
            // Fallback if placement test level is still set
            currentLevel = EnglishLevel.A1;
        }

        var levels = new List<EnglishLevel>
        {
            EnglishLevel.A1,
            EnglishLevel.A2,
            EnglishLevel.B1,
            EnglishLevel.B2,
            EnglishLevel.C1,
            EnglishLevel.C2
        };

        var steps = new List<PathStepDto>();
        int idCounter = 1;

        foreach (var lvl in levels)
        {
            // A1 and A2 should be filtered out or flagged as 'Completed via Placement Test'
            if (lvl < currentLevel)
            {
                // We filter them out to start exactly from their current assigned level up to C2
                continue;
            }

            string title;
            string desc;
            string status;
            int xpReward;

            switch (lvl)
            {
                case EnglishLevel.A1:
                    title = "Cấp độ A1 - Beginner";
                    desc = "Làm quen với các cấu trúc ngữ pháp cơ bản, từ vựng giao tiếp hàng ngày.";
                    xpReward = 100;
                    break;
                case EnglishLevel.A2:
                    title = "Cấp độ A2 - Elementary";
                    desc = "Nâng cao từ vựng và ngữ pháp về các chủ đề quen thuộc, đối thoại cơ bản.";
                    xpReward = 200;
                    break;
                case EnglishLevel.B1:
                    title = "Cấp độ B1 - Intermediate";
                    desc = "Mô tả kinh nghiệm, sự kiện, ước mơ và viết các văn bản đơn giản có tính liên kết.";
                    xpReward = 300;
                    break;
                case EnglishLevel.B2:
                    title = "Cấp độ B2 - Upper-Intermediate";
                    desc = "Hiểu các ý chính của văn bản phức tạp, giao tiếp trôi chảy với người bản xứ.";
                    xpReward = 450;
                    break;
                case EnglishLevel.C1:
                    title = "Cấp độ C1 - Advanced";
                    desc = "Hiểu các văn bản dài và khó, sử dụng ngôn ngữ linh hoạt cho các mục đích xã hội và học thuật.";
                    xpReward = 600;
                    break;
                case EnglishLevel.C2:
                    title = "Cấp độ C2 - Proficiency";
                    desc = "Dễ dàng hiểu hầu hết mọi văn bản nghe hoặc đọc, tóm tắt thông tin một cách mạch lạc.";
                    xpReward = 800;
                    break;
                default:
                    title = $"{lvl} Level";
                    desc = $"Adaptive learning content for CEFR Level {lvl}.";
                    xpReward = 100;
                    break;
            }

            if (lvl == currentLevel)
            {
                status = "Active";
                title += " (Current)";
            }
            else
            {
                status = "Locked";
            }

            steps.Add(new PathStepDto(idCounter++, title, desc, status, xpReward));
        }

        return ApiResponse<IEnumerable<PathStepDto>>.SuccessResponse(steps);
    }
}
