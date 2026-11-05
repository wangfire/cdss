# -*- coding: utf-8 -*-
"""给 V22PipelineOptions 加 CopyTo（手动字段复制，避免反射）。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

f = r"src\HospitalAi.Worker\Pipeline\V22PipelineOptions.cs"
c = open(f, encoding="utf-8").read()

if "public void CopyTo" in c:
    print("CopyTo already present")
    sys.exit(0)

# 在类定义结束前追加（找类体最后一个 "}" 前）
anchor = c.rstrip("\n")
assert anchor.endswith("}"), "class tail"
method = """
    /// <summary>
    /// 复制到另一实例（运行时覆盖基线用）：逐字段拷贝，新增开关字段时同步维护。
    /// </summary>
    public void CopyTo(V22PipelineOptions target)
    {
        target.IcdIndexEnabled = IcdIndexEnabled;
        target.IcdTitleDirectEnabled = IcdTitleDirectEnabled;
        target.IcdTitleReverseEnabled = IcdTitleReverseEnabled;
        target.Cm3IndexEnabled = Cm3IndexEnabled;
        target.Cm3TitleDirectEnabled = Cm3TitleDirectEnabled;
        target.VectorEnabled = VectorEnabled;
        target.RerankEnabled = RerankEnabled;
        target.RuleEngineEnabled = RuleEngineEnabled;
    }
"""
c = anchor[: len(anchor) - 1].rstrip("\n") + "\n" + method + "}\n"
open(f, "w", encoding="utf-8").write(c)
print("CopyTo added")
