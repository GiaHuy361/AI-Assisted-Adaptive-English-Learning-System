using System.Threading.Tasks;
using CoreLearningSystem.Domain.Entities;

namespace CoreLearningSystem.Application.Interfaces;

public interface ICertificateService
{
    Task<CertificateTestResult> RecordResultAsync(CertificateTestResult result);
}
