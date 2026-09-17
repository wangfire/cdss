using HospitalAi.Application.Abstractions;
using HospitalAi.Application.Common;
using HospitalAi.Contracts.Documents;
using HospitalAi.Contracts.Hospitals;
using HospitalAi.Contracts.Patients;
using HospitalAi.Contracts.Visits;
using HospitalAi.Domain.Visits;
using HospitalAi.Infrastructure.SqlServer;
using Microsoft.EntityFrameworkCore;

namespace HospitalAi.Infrastructure.ReferenceData;

/// <summary>
/// 医院、患者、就诊和文书的 SQL Server 服务。
/// </summary>
public sealed class SqlServerReferenceDataService(
    HospitalAiDbContext dbContext,
    IRequestContext requestContext) : IReferenceDataService
{
    public async Task<HospitalResponse> CreateHospitalAsync(
        CreateHospitalRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureText(request.Code, "医院编码");
        EnsureText(request.Name, "医院名称");

        var now = DateTimeOffset.UtcNow;
        var record = new HospitalRecord
        {
            Id = Guid.NewGuid(),
            Code = request.Code,
            Name = request.Name,
            Status = "ACTIVE",
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Hospitals.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new HospitalResponse(record.Id, record.Code, record.Name, record.Status, record.CreatedAt);
    }

    public async Task<PatientResponse> CreatePatientAsync(
        CreatePatientRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        EnsureText(request.SourceSystem, "来源系统");
        EnsureText(request.SourcePatientId, "来源患者标识");
        await EnsureHospitalExistsAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;
        var record = new PatientRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = requestContext.HospitalId,
            SourceSystem = request.SourceSystem,
            SourcePatientId = request.SourcePatientId,
            DisplayName = request.DisplayName,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Patients.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new PatientResponse(
            record.Id,
            record.HospitalId,
            record.SourceSystem,
            record.SourcePatientId,
            record.DisplayName,
            record.CreatedAt);
    }

    public async Task<VisitResponse> CreateVisitAsync(
        CreateVisitRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        var patient = await dbContext.Patients
            .SingleOrDefaultAsync(
                item => item.Id == request.PatientId
                    && item.HospitalId == requestContext.HospitalId,
                cancellationToken);

        if (patient is null)
        {
            throw new ResourceNotFoundException("患者记录不存在。");
        }

        var visit = Visit.Create(
            requestContext.HospitalId,
            request.PatientId,
            request.AdmissionAt,
            request.DischargeAt);
        var now = DateTimeOffset.UtcNow;
        var record = new VisitRecord
        {
            Id = visit.Id,
            HospitalId = visit.HospitalId,
            PatientId = visit.PatientId,
            AdmissionAt = visit.AdmissionAt,
            DischargeAt = visit.DischargeAt,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.Visits.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new VisitResponse(
            record.Id,
            record.HospitalId,
            record.PatientId,
            record.AdmissionAt,
            record.DischargeAt,
            record.CreatedAt);
    }

    public async Task<DocumentResponse> CreateDocumentAsync(
        CreateDocumentRequest request,
        CancellationToken cancellationToken = default)
    {
        EnsureHospital();
        EnsureText(request.DocumentType, "文书类型");
        EnsureText(request.ContentReference, "文书内容引用");
        EnsureText(request.ContentHash, "文书内容哈希");
        if (request.Version <= 0)
        {
            throw new ValidationException("文书版本必须大于零。");
        }

        var visitExists = await dbContext.Visits.AnyAsync(
            item => item.Id == request.VisitId
                && item.HospitalId == requestContext.HospitalId,
            cancellationToken);
        if (!visitExists)
        {
            throw new ResourceNotFoundException("就诊记录不存在。");
        }

        var now = DateTimeOffset.UtcNow;
        var record = new MedicalDocumentRecord
        {
            Id = Guid.NewGuid(),
            HospitalId = requestContext.HospitalId,
            VisitId = request.VisitId,
            DocumentType = request.DocumentType,
            ContentReference = request.ContentReference,
            ContentHash = request.ContentHash,
            Version = request.Version,
            CreatedAt = now,
            UpdatedAt = now
        };

        dbContext.MedicalDocuments.Add(record);
        await dbContext.SaveChangesAsync(cancellationToken);
        return new DocumentResponse(
            record.Id,
            record.VisitId,
            record.DocumentType,
            record.ContentReference,
            record.ContentHash,
            record.Version,
            record.CreatedAt);
    }

    private async Task EnsureHospitalExistsAsync(CancellationToken cancellationToken)
    {
        if (!await dbContext.Hospitals.AnyAsync(
                item => item.Id == requestContext.HospitalId,
                cancellationToken))
        {
            throw new ResourceNotFoundException("医院不存在。");
        }
    }

    private void EnsureHospital()
    {
        if (requestContext.HospitalId == Guid.Empty)
        {
            throw new ValidationException("X-Hospital-Id 不能为空。");
        }
    }

    private static void EnsureText(string? value, string fieldName)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ValidationException($"{fieldName}不能为空。");
        }
    }
}
