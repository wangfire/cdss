using HospitalAi.Domain.Common;
using HospitalAi.Domain.Visits;

namespace HospitalAi.Domain.Tests;

public sealed class VisitTests
{
    [Fact]
    public void Create_出院时间早于入院时间时_抛出领域异常()
    {
        var admissionAt = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        var dischargeAt = admissionAt.AddMinutes(-1);

        var exception = Assert.Throws<DomainException>(() =>
            Visit.Create(
                Guid.NewGuid(),
                Guid.NewGuid(),
                admissionAt,
                dischargeAt));

        Assert.Equal("出院时间不能早于入院时间。", exception.Message);
    }

    [Fact]
    public void Create_时间合法时_创建未出院就诊()
    {
        var admissionAt = new DateTimeOffset(2026, 9, 15, 10, 0, 0, TimeSpan.Zero);
        var hospitalId = Guid.NewGuid();
        var patientId = Guid.NewGuid();

        var visit = Visit.Create(hospitalId, patientId, admissionAt, null);

        Assert.NotEqual(Guid.Empty, visit.Id);
        Assert.Equal(hospitalId, visit.HospitalId);
        Assert.Equal(patientId, visit.PatientId);
        Assert.Equal(admissionAt, visit.AdmissionAt);
        Assert.Null(visit.DischargeAt);
    }
}
