using HospitalAi.Domain.Common;

namespace HospitalAi.Domain.Visits;

/// <summary>
/// 表示医院内患者的一次就诊。
/// </summary>
public sealed class Visit : Entity
{
    private Visit()
    {
    }

    private Visit(
        Guid id,
        Guid hospitalId,
        Guid patientId,
        DateTimeOffset admissionAt,
        DateTimeOffset? dischargeAt,
        DateTimeOffset now)
        : base(id, now)
    {
        HospitalId = hospitalId;
        PatientId = patientId;
        AdmissionAt = admissionAt;
        DischargeAt = dischargeAt;
    }

    public Guid HospitalId { get; private set; }

    public Guid PatientId { get; private set; }

    public DateTimeOffset AdmissionAt { get; private set; }

    public DateTimeOffset? DischargeAt { get; private set; }

    /// <summary>
    /// 创建就诊，并确保时间范围符合业务规则。
    /// </summary>
    public static Visit Create(
        Guid hospitalId,
        Guid patientId,
        DateTimeOffset admissionAt,
        DateTimeOffset? dischargeAt)
    {
        if (dischargeAt < admissionAt)
        {
            throw new DomainException("出院时间不能早于入院时间。");
        }

        return new Visit(
            Guid.NewGuid(),
            hospitalId,
            patientId,
            admissionAt,
            dischargeAt,
            DateTimeOffset.UtcNow);
    }
}
