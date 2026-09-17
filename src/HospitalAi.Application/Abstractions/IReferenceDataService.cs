using HospitalAi.Contracts.Documents;
using HospitalAi.Contracts.Hospitals;
using HospitalAi.Contracts.Patients;
using HospitalAi.Contracts.Visits;

namespace HospitalAi.Application.Abstractions;

/// <summary>
/// 医院、患者、就诊和文书的最小应用服务契约。
/// </summary>
public interface IReferenceDataService
{
    Task<HospitalResponse> CreateHospitalAsync(
        CreateHospitalRequest request,
        CancellationToken cancellationToken = default);

    Task<PatientResponse> CreatePatientAsync(
        CreatePatientRequest request,
        CancellationToken cancellationToken = default);

    Task<VisitResponse> CreateVisitAsync(
        CreateVisitRequest request,
        CancellationToken cancellationToken = default);

    Task<DocumentResponse> CreateDocumentAsync(
        CreateDocumentRequest request,
        CancellationToken cancellationToken = default);
}
