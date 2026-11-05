using HospitalAi.Application.Coding.Preprocessing;
var text1 = "【出院诊断】：急性阑尾炎。【手术操作】：阑尾切除术。";
var text2 = "患者因转移性右下腹痛伴发热入院，查体麦氏点压痛阳性，反跳痛阳性。术中见阑尾充血水肿，表面脓性渗出，行腹腔镜阑尾切除术，术后病理示急性化脓性阑尾炎。术后予抗感染治疗，恢复良好出院。";
foreach (var (label, text) in new[] { ("DISCHARGE", text1), ("ADMISSION", text2) })
{
    var chunks = DocumentChunker.Chunk(text);
    Console.WriteLine($"{label}: chunks={chunks.Count}");
    foreach (var c in chunks) Console.WriteLine($"  [{c.StartPosition}-{c.EndPosition}] {c.Text}");
    var facts = ClinicalFactExtractor.Extract(chunks, null);
    Console.WriteLine($"  facts={facts.Count}");
    foreach (var f in facts) Console.WriteLine($"    type={f.FactType} name={f.FactName}");
}
