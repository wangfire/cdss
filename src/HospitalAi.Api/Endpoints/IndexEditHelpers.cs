using HospitalAi.Application.Common;

namespace HospitalAi.Api.Endpoints;

/// <summary>
/// 索引库编辑端点（导入 / 单行增改 / 重建）共用的工具方法。
/// 由 Program.cs 拆分而来，此前是 Program.cs 顶层的局部函数。
/// </summary>
internal static class IndexEditHelpers
{
    /// <summary>索引库编辑端点共用：空白视为未填（null），其余去除首尾空白。</summary>
    public static string? TrimToNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>
    /// 父链防环：新父节点不能是自己或自己的子孙（沿新父节点向上走，遇到自身即环）。
    /// </summary>
    public static async Task EnsureNoIndexParentCycleAsync(
        int recordSeqNo,
        int parentSeqNo,
        Func<int, Task<int?>> readParentSeqNo,
        CancellationToken cancellationToken)
    {
        var cursor = parentSeqNo;
        for (var hops = 0; cursor > 0 && hops < 32; hops++)
        {
            if (cursor == recordSeqNo)
            {
                throw new ValidationException("父节点不能是自身或其子孙节点。");
            }

            var next = await readParentSeqNo(cursor);
            if (next is null)
            {
                break;
            }

            cursor = next.Value;
        }
    }
}
