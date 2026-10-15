using HospitalAi.Application.Coding.Matching;

namespace HospitalAi.Application.Tests;

/// <summary>
/// 术语同义词等价类快照单测：等同边按连通分量合并、类内任意两词互换、
/// 单字符成员（"术"、数字/罗码分度）不入改写规则、通道互不串用。
/// </summary>
public sealed class IcdTermSynonymSnapshotTests
{
    private static IcdTermSynonymRow Row(string canonical, string variant, string termType = TermSynonymTypes.Diagnosis) =>
        new(termType, canonical, variant);

    [Fact]
    public void 同组多个变体词构成等价类_任意两词可互换()
    {
        var snapshot = IcdTermSynonymSnapshot.Build([
            Row("期前收缩", "早搏"),
            Row("期前收缩", "除极"),
        ]);

        Assert.Contains("期前收缩", snapshot.ExpandVariants("早搏", TermSynonymTypes.Diagnosis));
        Assert.Contains("除极", snapshot.ExpandVariants("早搏", TermSynonymTypes.Diagnosis));
        Assert.Contains("期前收缩", snapshot.ExpandVariants("除极", TermSynonymTypes.Diagnosis));
        Assert.Contains("早搏", snapshot.ExpandVariants("除极", TermSynonymTypes.Diagnosis));
        // 输入已用类标签词时，仍可改写到类内其他成员（索引另一种写法的情况）。
        Assert.Contains("心律失常，早搏", snapshot.ExpandVariants("心律失常，期前收缩", TermSynonymTypes.Diagnosis));
        Assert.Contains("心律失常，除极", snapshot.ExpandVariants("心律失常，期前收缩", TermSynonymTypes.Diagnosis));
    }

    [Fact]
    public void 链式等同边合并为一个类()
    {
        // 乳腺→乳房、乳房→早跳 两条边把三个词连成同一类。
        var snapshot = IcdTermSynonymSnapshot.Build([
            Row("乳房", "乳腺"),
            Row("早跳", "乳房"),
        ]);

        var expanded = snapshot.ExpandVariants("乳腺", TermSynonymTypes.Diagnosis);
        Assert.Contains("早跳", expanded);
        Assert.Equal(3, expanded.Count); // 原词 + 类标签 + 另一个成员
    }

    [Fact]
    public void 镜像成对登记不产生重复改写()
    {
        var snapshot = IcdTermSynonymSnapshot.Build([
            Row("乳房", "乳腺"),
            Row("乳腺", "乳房"),
        ]);

        // 类标签取被引用次数相同、ordinal 更小的 乳房，与原输入合计两条检索文本。
        Assert.Equal(["乳腺", "乳房"], snapshot.ExpandVariants("乳腺", TermSynonymTypes.Diagnosis));
    }

    [Fact]
    public void 长词优先占位_短词不再二次改写()
    {
        var snapshot = IcdTermSynonymSnapshot.Build([
            Row("期前收缩", "早搏"),
            Row("期前收缩", "过早搏动"),
        ]);

        // "过早搏动" 命中后整段占位，不能被其中的 "早搏" 二次改写。
        var expanded = snapshot.ExpandVariants("频发过早搏动", TermSynonymTypes.Diagnosis);
        Assert.Equal(["频发过早搏动", "频发期前收缩", "频发早搏"], expanded);
    }

    [Fact]
    public void 单字符成员不参与改写()
    {
        var snapshot = IcdTermSynonymSnapshot.Build([
            Row("手术后", "术"),
            Row("手术后", "术后"),
            Row("3", "Ⅲ"),
            Row("3", "III"),
        ]);

        // "术" 既不作锚点也不作目标，"手术" 不会被改成 "手术后"。
        Assert.Equal(["阑尾切除术"], snapshot.ExpandVariants("阑尾切除术", TermSynonymTypes.Diagnosis));
        Assert.Equal(["心功能Ⅲ级"], snapshot.ExpandVariants("心功能Ⅲ级", TermSynonymTypes.Diagnosis));
        Assert.Equal(["心功能III级"], snapshot.ExpandVariants("心功能III级", TermSynonymTypes.Diagnosis));
        // 两字成员仍按类互换：术后 → 手术后（类标签）。
        Assert.Equal(["阑尾切除术后", "阑尾切除手术后"], snapshot.ExpandVariants("阑尾切除术后", TermSynonymTypes.Diagnosis));
    }

    [Fact]
    public void 通道分开_手术通道不受诊断同义词影响()
    {
        var snapshot = IcdTermSynonymSnapshot.Build([
            Row("期前收缩", "早搏"),
            Row("冠状动脉旁路移植术", "搭桥手术", TermSynonymTypes.Procedure),
        ]);

        Assert.Equal(["早搏"], snapshot.ExpandVariants("早搏", TermSynonymTypes.Procedure));
        Assert.Equal(["搭桥手术", "冠状动脉旁路移植术"], snapshot.ExpandVariants("搭桥手术", TermSynonymTypes.Procedure));
    }

    [Fact]
    public void 展开文本数受上限约束()
    {
        var snapshot = IcdTermSynonymSnapshot.Build([
            Row("期前收缩", "早搏"),
            Row("期前收缩", "早跳"),
            Row("期前收缩", "过早搏动"),
            Row("期前收缩", "过早收缩"),
            Row("期前收缩", "除极"),
            Row("期前收缩", "去极化"),
        ]);

        Assert.True(snapshot.ExpandVariants("早搏", TermSynonymTypes.Diagnosis).Count <= 8);
    }

    [Theory]
    [InlineData("心功能Ⅴ级", "心功能5级")]
    [InlineData("心功能V级", "心功能5级")]
    [InlineData("心功能III级", "心功能3级")]
    [InlineData("心功能Ⅲ级", "心功能3级")]
    [InlineData("心功能3级", "心功能3级")]
    [InlineData("心功能IV级", "心功能4级")]
    [InlineData("心功能Ⅳ级", "心功能4级")]
    [InlineData("肾囊肿III", "肾囊肿3")]
    // 紧跟在字母后的 ASCII 罗码不转数字，保住 "i细胞"、"nyhaiv" 这类真实字母用法。
    [InlineData("NYHA IV", "nyhaiv")]
    [InlineData("抗ssai", "抗ssai")]
    public void 分度写法归一到同一键(string text, string expected)
        => Assert.Equal(expected, TextMatch.NormalizeKey(text));

    /// <summary>
    /// ASCII 罗码只识别到 4 位：分度范围 "II-III期" 剥标点后会连成 iiiii，
    /// 若按 5 位识别会把范围误并成 5 级（回归中因此丢掉 E11.301+H36.0* 的 Top-3 命中）。
    /// </summary>
    [Fact]
    public void 范围写法不被误并成五级()
        => Assert.DoesNotContain('5', TextMatch.NormalizeKey("2型糖尿病视网膜病变II-III期"));
}
