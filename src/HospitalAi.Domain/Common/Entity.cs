namespace HospitalAi.Domain.Common;

/// <summary>
/// 领域实体基类，统一维护实体标识和审计时间。
/// </summary>
public abstract class Entity
{
    protected Entity()
    {
    }

    protected Entity(Guid id, DateTimeOffset now)
    {
        Id = id;
        CreatedAt = now;
        UpdatedAt = now;
    }

    public Guid Id { get; protected init; }

    public DateTimeOffset CreatedAt { get; protected init; }

    public DateTimeOffset UpdatedAt { get; protected private set; }

    /// <summary>
    /// 状态发生变化时刷新更新时间。
    /// </summary>
    protected void Touch(DateTimeOffset now)
    {
        UpdatedAt = now;
    }
}
