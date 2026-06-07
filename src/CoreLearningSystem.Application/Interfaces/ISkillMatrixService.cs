using System.Threading;
using System.Threading.Tasks;
using CoreLearningSystem.Application.DTOs.Common;

namespace CoreLearningSystem.Application.Interfaces;

public interface ISkillMatrixService
{
    Task<SkillMatrixUpdateResult> UpdateSkillMatrixAsync(SkillMatrixUpdateRequest request, CancellationToken cancellationToken);
}
