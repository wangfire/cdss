namespace HospitalAi.Application.Coding.Matching;

/// <summary>
/// 临床文本匹配公共工具：归一化键、连续片段覆盖率、部位冲突与糖尿病复合词判定。
/// 字典直匹配（手术通道）与 ICD 索引链路共用同一套门控口径，避免两套阈值漂移。
/// </summary>
public static class TextMatch
{
    private static readonly System.Collections.Immutable.ImmutableDictionary<char, string> RomanNumeralDigits =
        System.Collections.Immutable.ImmutableDictionary.CreateRange<char, string>([
            new('Ⅰ', "1"), new('Ⅱ', "2"), new('Ⅲ', "3"), new('Ⅳ', "4"), new('Ⅴ', "5"),
            new('Ⅵ', "6"), new('Ⅶ', "7"), new('Ⅷ', "8"), new('Ⅸ', "9"), new('Ⅹ', "10"),
            new('ⅰ', "1"), new('ⅱ', "2"), new('ⅲ', "3"), new('ⅳ', "4"), new('ⅴ', "5"),
            new('ⅵ', "6"), new('ⅶ', "7"), new('ⅷ', "8"), new('ⅸ', "9"), new('ⅹ', "10"),
        ]);

    // ASCII 罗码串只在其后是分度语境（度/级/型/期/类）或行尾时才转数字，
    // 保住 "i细胞"、"X线" 这类真实字母用法不被误改。
    // 上限 4 位：分度范围常写作 "II-III期"，标点剥离后连成 iiiii，若按 5 位识别会把范围误并成 5 级。
    private static readonly System.Text.RegularExpressions.Regex RomanRunRegex =
        new(@"(?<![a-z0-9])(xiv|ix|iv|vi{0,3}|i{1,4})(?=[度级型期类]|$)",
            System.Text.RegularExpressions.RegexOptions.Compiled);

    /// <summary>
    /// 归一化键：去标点空白、转小写，并统一分度标记写法——
    /// Unicode 罗码（Ⅲ）直接转数字，ASCII 罗码串（III/ii）在 度/级/型/期/类 语境转数字，
    /// 使 "心功能Ⅲ级"、"心功能III级"、"心功能3级" 得到同一个键。
    /// </summary>
    public static string NormalizeKey(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return string.Empty;
        }

        var builder = new System.Text.StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (char.IsPunctuation(character) || char.IsWhiteSpace(character))
            {
                continue;
            }

            if (RomanNumeralDigits.TryGetValue(character, out var digit))
            {
                builder.Append(digit);
                continue;
            }

            builder.Append(char.ToLowerInvariant(character));
        }

        return RomanRunRegex.Replace(
            builder.ToString(),
            match => RomanWordToDigit(match.Groups[1].Value));
    }

    private static string RomanWordToDigit(string run)
    {
        var value = 0;
        var index = 0;
        foreach (var (symbol, amount) in new[]
        {
            ("ix", 9), ("iv", 4), ("viii", 8), ("vii", 7), ("vi", 6), ("v", 5),
            ("iiii", 4), ("iii", 3), ("ii", 2), ("i", 1),
        })
        {
            while (index + symbol.Length <= run.Length
                && string.CompareOrdinal(run, index, symbol, 0, symbol.Length) == 0)
            {
                value += amount;
                index += symbol.Length;
            }
        }

        return index == run.Length && value > 0 ? value.ToString() : run;
    }

    public readonly record struct Coverage(decimal Ratio, int MaxSegment, int CoveredChars);

    /// <summary>
    /// fragment 被 text 覆盖的连续片段比例：反复取二者的最长公共子串（≥2），
    /// 累计 fragment 中被覆盖的字符比例；单字符 fragment 退化为包含判定。
    /// </summary>
    public static Coverage CoverageOf(string fragmentKey, string textKey)
        => CoverageOf(fragmentKey, textKey, countSingleCharSegments: false);

    /// <summary>
    /// countSingleCharSegments=true：残存单字符按包含计覆盖。
    /// 仅供索引下钻遍历使用（"钙化→肾" 类单字部位词需要参与增益），
    /// 输出侧门控（MatchesInput/字典展开）保持 ≥2 连续段口径，避免单点误命中。
    /// </summary>
    public static Coverage CoverageOf(string fragmentKey, string textKey, bool countSingleCharSegments)
    {
        if (fragmentKey.Length == 0)
        {
            return new Coverage(0m, 0, 0);
        }

        if (textKey.Length == 0)
        {
            return new Coverage(0m, 0, 0);
        }

        if (fragmentKey.Length == 1)
        {
            var hit = textKey.Contains(fragmentKey, StringComparison.Ordinal);
            return new Coverage(hit ? 1m : 0m, hit ? 1 : 0, hit ? 1 : 0);
        }

        var covered = new bool[fragmentKey.Length];
        var coveredCount = 0;
        var maxSegment = 0;
        var currentText = textKey;
        var singlePassDone = false;
        while (true)
        {
            var (start, length) = LongestCommonSubstring(fragmentKey, currentText);
            if (length == 0
                || (length == 1 && (singlePassDone || !countSingleCharSegments)))
            {
                break;
            }

            if (length == 1)
            {
                // LCS 单调不增：首次出现 1 后本轮清完全部单字符即终止。
                singlePassDone = true;
            }

            maxSegment = Math.Max(maxSegment, length);
            for (var index = start; index < start + length; index++)
            {
                if (!covered[index])
                {
                    covered[index] = true;
                    coveredCount++;
                }
            }

            currentText = currentText.Remove(
                currentText.IndexOf(fragmentKey.AsSpan(start, length)), length);
            if (currentText.Length == 0)
            {
                break;
            }
        }

        return new Coverage(coveredCount / (decimal)fragmentKey.Length, maxSegment, coveredCount);
    }

    /// <summary>
    /// 词条能否命中输入：覆盖度 ≥0.6 且（最长连续段 ≥3 或全覆盖）。
    /// 短词条（≤2 字）要求整词包含，挡住 "硬化" 这类单点误命中。
    /// </summary>
    public static bool MatchesInput(string termKey, string inputKey, out Coverage coverage)
    {
        coverage = CoverageOf(termKey, inputKey);
        if (termKey.Length <= 2)
        {
            return coverage.Ratio >= 1.0m && inputKey.Contains(termKey, StringComparison.Ordinal);
        }

        return coverage.Ratio >= 0.6m
            && (coverage.MaxSegment >= 3 || coverage.Ratio >= 1.0m);
    }

    /// <summary>字符逆序镜像：索引拼接路径与字典标题语序相反时的覆盖比对用。</summary>
    public static string Reverse(string text)
    {
        var characters = text.ToCharArray();
        Array.Reverse(characters);
        return new string(characters);
    }

    /// <summary>
    /// fragment 通过 ≥2 连续公共片段在 text 中占住的字符位置集合（反复取 LCS 并把已占位掩掉）。
    /// 下钻"新内容"判定用：位置口径才能区分 "先兆流产→伴有随后性流产"（流产 已被路径占过）
    /// 与 "阻塞性→慢性"（慢性 是输入里的新片段）。
    /// </summary>
    public static HashSet<int> CoveredTextPositions(string fragmentKey, string textKey)
    {
        var positions = new HashSet<int>();
        if (fragmentKey.Length < 2 || textKey.Length < 2)
        {
            return positions;
        }

        var mask = textKey.ToCharArray();
        while (true)
        {
            var masked = new string(mask);
            var (start, length) = LongestCommonSubstring(masked, fragmentKey);
            if (length < 2)
            {
                return positions;
            }

            for (var index = start; index < start + length; index++)
            {
                mask[index] = '\0';
                positions.Add(index);
            }
        }
    }

    public static (int Start, int Length) LongestCommonSubstring(string left, string right)    {
        if (left.Length == 0 || right.Length == 0)
        {
            return (0, 0);
        }

        var previous = new int[right.Length + 1];
        var bestStart = 0;
        var bestLength = 0;
        for (var i = left.Length - 1; i >= 0; i--)
        {
            var currentRow = new int[right.Length + 1];
            for (var j = right.Length - 1; j >= 0; j--)
            {
                if (left[i] == right[j])
                {
                    currentRow[j] = previous[j + 1] + 1;
                    if (currentRow[j] > bestLength)
                    {
                        bestLength = currentRow[j];
                        bestStart = i;
                    }
                }
            }

            previous = currentRow;
        }

        return (bestStart, bestLength);
    }

    /// <summary>
    /// 标题对输入的字符覆盖率（输入视角），用于字典候选精排门控。
    /// </summary>
    public static bool TitleCoversInput(string? title, string? inputText)
    {
        var normalizedTitle = NormalizeKey(title);
        var normalizedInput = NormalizeKey(inputText);
        if (normalizedTitle.Length == 0 || normalizedInput.Length == 0)
        {
            return false;
        }

        var titleCharacters = new HashSet<char>(normalizedTitle);
        var inputCharacters = normalizedInput.Distinct().ToArray();
        var covered = inputCharacters.Count(character => titleCharacters.Contains(character));
        return (decimal)covered / inputCharacters.Length >= 0.6m;
    }

    /// <summary>
    /// 证据是否逐字支持该标题（≥2 字标题必须原样出现在证据原文中）。
    /// 模型引用证据时必须能被这条确定性规则复核，否则引用不计为有效。
    /// </summary>
    public static bool EvidenceSupportsTitle(string? evidenceText, string title)
    {
        return title.Length >= 2
            && !string.IsNullOrWhiteSpace(evidenceText)
            && evidenceText.Contains(title, StringComparison.Ordinal);
    }

    public static readonly string[] BodyQualifiers =
        ["颈", "胸", "腰", "骶", "尾", "肩", "肘", "腕", "髋", "膝", "踝", "足", "手",
            "头", "腹", "耳", "鼻", "牙", "眼", "角膜", "妊娠",
            "肺", "骨", "关节", "肾", "肝", "脾", "胆", "胰", "膀胱", "子宫", "卵巢",
            "甲状腺", "脊", "椎", "腱", "肌", "视网膜", "玻璃体", "巩膜", "结膜", "颅", "皮肤",
            "牙源性", "腺", "前列腺", "阴道", "睾丸", "阴茎", "外阴", "乳腺",
            "输卵管", "脑", "肝", "食管", "气管", "支气管", "胸膜", "腹膜", "结肠", "直肠", "阑尾",
            "精囊", "基底节", "基底", "结核", "腱索"];

    /// <summary>
    /// 候选标题声明了输入没有的部位/特殊人群限定词即视为冲突剔除；
    /// 标题不含限定词则放行。
    /// </summary>
    public static bool HasBodyPartConflict(string? title, string? inputText)
    {
        var titleQualifiers = ExtractQualifiers(title);
        return titleQualifiers.Count > 0
            && !titleQualifiers.IsSubsetOf(ExtractQualifiers(inputText));
    }

    public static HashSet<string> ExtractQualifiers(string? text)
    {
        var normalized = NormalizeKey(text);
        return new HashSet<string>(
            BodyQualifiers.Where(word => normalized.Contains(word, StringComparison.Ordinal)),
            StringComparer.Ordinal);
    }

    /// <summary>
    /// 病程/时限互斥对：索引子树常用 "急性或…4周内 / 慢性或…超过4周" 这种成对分支，
    /// 只靠字符增益会让 "急性心肌梗死" 一路下钻到慢性分支（I25.8）。
    /// </summary>
    private static readonly (string Left, string Right)[] ExclusivePhasePoles =
        [("急性", "慢性"), ("近期", "陈旧")];

    /// <summary>
    /// 节点声明某一极（急性/慢性…）而输入只声明其反极 → 语义矛盾，禁止命中该节点。
    /// 双方都声明或都未声明均放行（"慢性…急性加重" 不冲突）。
    /// </summary>
    public static bool HasPhasePoleConflict(string? nodeText, string? inputText)
    {
        var nodeKey = NormalizePhaseKey(nodeText);
        var inputKey = NormalizePhaseKey(inputText);
        if (nodeKey.Length == 0 || inputKey.Length == 0)
        {
            return false;
        }

        foreach (var (left, right) in ExclusivePhasePoles)
        {
            if ((DeclaresOnly(nodeKey, left, right) && DeclaresOnly(inputKey, right, left))
                || (DeclaresOnly(nodeKey, right, left) && DeclaresOnly(inputKey, left, right)))
            {
                return true;
            }
        }

        return false;
    }

    // "亚急性" 含 "急性"，先屏蔽避免把它当成急性极声明。
    private static string NormalizePhaseKey(string? text)
        => NormalizeKey(text).Replace("亚急性", "亚");

    private static bool DeclaresOnly(string key, string word, string opposite)
        => key.Contains(word, StringComparison.Ordinal)
            && !key.Contains(opposite, StringComparison.Ordinal);

    /// <summary>
    /// 索引检索噪声词：待查/待定类引导语、程度修饰、并列连接词。
    /// 链路重叠门槛的分母不计这些字符——"轻度贫血原因待查" 的合法链路只有 贫血 两字，
    /// 按整条输入稀释会掉到门槛之下（原因待查类输入是临床书写惯例，不是匹配失败）。
    /// 顺序敏感：长语料在前，先删 "原因待查" 再删 "待查"。
    /// </summary>
    private static readonly string[] IndexingFillerPhrases =
    [
        "原因待查", "原因不明", "病因不明", "可能性大", "不除外", "待查", "待排", "待定", "可能", "考虑",
        "轻度", "中度", "重度", "伴", "并", "及", "与", "和", "性质",
    ];

    /// <summary>
    /// 不确定性引导语：诊断名里的 可能性大/待查/不除外 是医生对**诊断成立与否**的标注，
    /// 与编码要选的病变概念无关，且其字面会反向污染候选行打分——
    /// "十二指肠溃疡伴出血可能性大" 的 性 让 慢性十二指肠溃疡伴有出血 白得一分，
    /// 把与输入逐字相等的 K26.400X001 挤到 Top-2。候选行覆盖度按剥掉这类词的输入核计算。
    /// 程度词（轻/中/重度）不在此列：它们是有语义的限定，剥了会让 轻度 与 重度 标题并列。
    /// </summary>
    private static readonly string[] UncertaintyPhrases =
    [
        "原因待查", "原因不明", "病因不明", "可能性大", "不除外", "待查", "待排", "待定", "可能", "考虑", "性质",
    ];

    /// <summary>剥离检索噪声词后的检索核（归一化键）。</summary>
    public static string StripIndexingFillers(string? text) => StripPhrases(text, IndexingFillerPhrases);

    /// <summary>只剥不确定性引导语后的检索核，用于候选行覆盖度打分。</summary>
    public static string StripUncertaintyPhrases(string? text) => StripPhrases(text, UncertaintyPhrases);

    private static string StripPhrases(string? text, string[] phrases)
    {
        var key = NormalizeKey(text);
        foreach (var phrase in phrases)
        {
            key = key.Replace(phrase, string.Empty, StringComparison.Ordinal);
        }

        return key;
    }

    /// <summary>
    /// 星号复合码标题含 "糖尿病" 而输入未提糖尿病 → 糖尿病污染候选，剔除。
    /// </summary>
    public static bool IsUnjustifiedDiabetesCompound(string? title, string? inputText)
    {
        var titleKey = NormalizeKey(title);
        if (!titleKey.Contains("糖尿病", StringComparison.Ordinal))
        {
            return false;
        }

        var inputKey = NormalizeKey(inputText);
        return !inputKey.Contains("糖尿病", StringComparison.Ordinal);
    }

    /// <summary>索引/字典里糖尿病分型的互斥标记（1型/2型经 NormalizeKey 已统一罗码写法）。</summary>
    private static readonly string[] DiabetesTypeMarkers =
        ["1型", "2型", "3型", "妊娠", "继发性", "营养不良性", "营养缺乏性", "其他特指"];

    /// <summary>
    /// 糖尿病分型互斥：输入与候选行各自声明了**不同**的分型即冲突（2型 ≠ 继发性 ≠ 1型）。
    /// <paramref name="resolveTypeFromCategoryAndDefault"/> 只在索引书给出分型替代组
    /// （E10.3;E11.3;…;E14.3 一类）时置真：此时标题不写分型可按类目定性
    /// （ICD-10 里 E10~E14 本身就是分型），输入未写分型则按病案科默认口径视作 2型——
    /// 本地 10000 条临床诊断名实测：含"糖尿病"且未标分型的 87 个名字里 69 个（79%）
    /// 源编码落在 E11，落 E14（未特指）只有 5 个。
    /// 替代组之外不套这层：主导词 糖尿病(性) 自身的编码就是 E14.-（未特指），
    /// 套默认会把裸输入"糖尿病"的正确未特指推荐打成噪声。
    /// 输入声明多型（如 "1型或2型糖尿病"）时不套默认，交给覆盖度打分。
    /// </summary>
    public static bool HasDiabetesTypeConflict(
        string? title, string? code, string? inputText, bool resolveTypeFromCategoryAndDefault)
    {
        var titleType = DeclaredDiabetesType(title)
            ?? (resolveTypeFromCategoryAndDefault ? DiabetesCategoryType(code) : null);
        var inputType = DeclaredDiabetesType(inputText)
            ?? (resolveTypeFromCategoryAndDefault ? SilentDiabetesType(inputText) : null);
        return titleType is not null && inputType is not null
            && !string.Equals(titleType, inputType, StringComparison.Ordinal);
    }

    /// <summary>类目即分型：E10 1型 / E11 2型 / E12 3型 / E13 其他特指 / E14 未特指。</summary>
    private static string? DiabetesCategoryType(string? code)
    {
        var key = NormalizeKey(code);
        return key.StartsWith("e10", StringComparison.Ordinal) ? "1型"
            : key.StartsWith("e11", StringComparison.Ordinal) ? "2型"
            : key.StartsWith("e12", StringComparison.Ordinal) ? "3型"
            : key.StartsWith("e13", StringComparison.Ordinal) ? "其他特指"
            : key.StartsWith("e14", StringComparison.Ordinal) ? "未特指"
            : null;
    }

    /// <summary>
    /// 只提"糖尿病"完全不提分型时的默认口径（2型）。
    /// 写了多个分型（"1型或2型糖尿病"）不算沉默，不能套默认。
    /// </summary>
    private static string? SilentDiabetesType(string? text)
    {
        var key = NormalizeKey(text);
        if (!key.Contains("糖尿病", StringComparison.Ordinal))
        {
            return null;
        }

        return DiabetesTypeMarkers.Any(marker => key.Contains(marker)) ? null : "2型";
    }

    private static string? DeclaredDiabetesType(string? text)
    {
        var key = NormalizeKey(text);
        if (!key.Contains("糖尿病", StringComparison.Ordinal))
        {
            return null;
        }

        string? declared = null;
        foreach (var marker in DiabetesTypeMarkers.Where(key.Contains))
        {
            if (declared is not null)
            {
                return null;
            }

            declared = marker;
        }

        return declared;
    }

    /// <summary>
    /// 否定极冲突：输入写 "非X"（非增殖性/非小细胞/非典型）而候选标题只写 X，
    /// 或标题写 非X 而输入只写 X——两者是互斥的临床分期，不是近义。
    /// 子串匹配天然会把 "轻度非增殖性糖尿病视网膜病变" 判给 E11.300X031
    /// 2型糖尿病性增殖性视网膜病（标题的 增殖性 完整含在输入里），与临床语义相反。
    /// 被否定的限定词取 "非" 之后**连续汉字**的 2~8 字前缀逐长试探，
    /// 使 非增殖性 同时挡住 增殖 与 增殖性 打头的详码行；
    /// 任一侧已带 "非" 前缀（同侧否定）即放行，不判成矛盾。
    /// </summary>
    public static bool HasNegationPoleConflict(string? title, string? inputText)
    {
        var titleKey = NormalizeKey(title);
        var inputKey = NormalizeKey(inputText);
        return NegatesTermOf(inputKey, titleKey) || NegatesTermOf(titleKey, inputKey);
    }

    private static bool NegatesTermOf(string negatedSide, string otherSide)
    {
        if (negatedSide.Length < 3 || otherSide.Length == 0)
        {
            return false;
        }

        for (var position = negatedSide.IndexOf('非');
             position >= 0 && negatedSide.Length - position > 2;
             position = negatedSide.IndexOf('非', position + 1))
        {
            var available = 0;
            while (available < 8 && position + 1 + available < negatedSide.Length
                && IsHanCharacter(negatedSide[position + 1 + available]))
            {
                available++;
            }

            for (var candidate = 2; candidate <= available; candidate++)
            {
                var term = negatedSide.Substring(position + 1, candidate);
                if (otherSide.Contains(term, StringComparison.Ordinal)
                    && !DeclaresNegation(otherSide, term))
                {
                    return true;
                }
            }
        }

        return false;
    }

    /// <summary>另一侧对该限定词也做了否定（非/不/未/无 开头）就不是矛盾："非典型增生" 与 "不典型增生" 同义。</summary>
    private static bool DeclaresNegation(string otherSide, string term)
        => NegationPrefixes.Any(prefix => otherSide.Contains(prefix + term, StringComparison.Ordinal));

    private static readonly string[] NegationPrefixes = ["非", "不", "未", "无"];

    private static bool IsHanCharacter(char character)
        => character is >= '一' and <= '龥';
}
