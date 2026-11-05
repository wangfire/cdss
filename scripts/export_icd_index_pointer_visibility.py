# -*- coding: utf-8 -*-
"""ICD 索引库全量导出 + 见/另见指针可见性判定。

口径与 IcdIndexModels.ParseNode / IcdIndexMatcher.ResolveJumpLead 对齐：
- 指针形态：横杠 `- 见/另见 X`、结尾圆括号 `(另见 X)`（括号只认另见，见纪律1）；
  现译名 Redirect 也一并标出。
- 主导词表：0 级且 JumpKind∈{None,SeeAlso}（IsValidLead），键=词面归一 + 或/，、切段归一。
- 目标解析：按 ，,、 切段，剥 <2 字残段（ResolveJumpLead 同款），情况→疾病；
  先查首段精确键，再查全段拼接键。
- 可见性（用户口径）：目标主导词能解析到，且首段之外的修饰段在该主导词子树内可达 → 是；
  目标本身含编码 token、或为 泛词(情况/疾病/病) → 是；解析不到/修饰段不可达/自环死指针 → 否。
- 自环死指针（纪律12/19）：解析回自身或祖先 且 本行无码无子行 → 否。
- 简化项：ResolveJumpLead 的 RecallLeads 模糊兜底不实现（找不到即否）。目标修饰段与索引行同走 alias_keys 建键（方括号剥除/或，、切段/剥尾码），保证两侧键口径一致。
"""
import re
import sys
import unicodedata
from collections import Counter, deque

import openpyxl
from openpyxl.styles import Font, PatternFill

SRC = 'logs/_icd_full.tsv'
EN_SRC = 'logs/_icd_en.tsv'
OUT = 'data/exports/2026-11-02-ICD索引库-见另见指针可见性版.xlsx'

DASHES = '-－—–﹣‐‑‒−―'
JUMP_RE = re.compile(r'\s*[-－—–\s]\s*(另见|见)\s*(.+)$')
PAREN_JUMP_RE = re.compile(r'[（(]\s*另见\s*(.+?)\s*[)）]\s*$')
REDIRECT_RE = re.compile(r'\s*[-－—–\s]*现译名\s*(.+)$')
TAIL_ANOTHER_RE = re.compile(r'[，、,]\s*又称\s*[^，、,]+\s*$')
CODE_TOKEN_RE = re.compile(r'[A-Z]\d{2}')
SPLIT_RE = re.compile(r'[或，、,]')

# 行为词由形态学行为码（/3 /2 /0 /1 /6）承载，不作为表内子行参与可达性
BEHAVIOR = {'恶性', '良性', '原位', '动态未定', '行为未定', '继发性', '未知'}
TUMOR_TRIGGER_HEADS = {'肿瘤', '肿瘤表'}

ROMAN = {'Ⅰ': '1', 'Ⅱ': '2', 'Ⅲ': '3', 'Ⅳ': '4', 'Ⅴ': '5', 'Ⅵ': '6',
         'Ⅶ': '7', 'Ⅷ': '8', 'Ⅸ': '9', 'Ⅹ': '10', 'Ⅺ': '11', 'Ⅻ': '12'}
GENERIC = {'情况', '疾病', '病'}


def norm(text: str) -> str:
    out = []
    for ch in text:
        if ch in ROMAN:
            out.append(ROMAN[ch])
            continue
        if unicodedata.category(ch).startswith(('P', 'Z')) or ch.isspace():
            continue
        out.append(ch.lower())
    return ''.join(out)


# 词面里嵌入的尾部编码："骨折(…)  T14.2"、"E74.0? I43.1*"、" P55.1"
TRAILING_CODE_RE = re.compile(r'\s+[A-Z]\d{2}[0-9?.*\sA-Z]*$')
PAREN_RE = re.compile(r'[（(][^（()）]*[）)]')
DIRECTIVE_PAREN_RE = re.compile(r'[（(][^（()）]*(?:另?见)[^（()）]*[）)]')
MORPH_PAREN_RE = re.compile(r'[（(]\s*M\d{4}\s*/\s*\d\s*[）)]')
SQUARE_RE = re.compile(r'[［\[]([^\]］]*)[\]］]')
NEC_RE = re.compile(r'(?i)\s*\b(NEC|NOS)\b\s*')


def alias_keys(text: str) -> tuple[set[str], set[str]]:
    """ParseAliases 同款主流程：剥方括号/指令括号/形态码括号 → 或，、切段 → 全段+剥括号段，键=归一。
    返回 (全部键, 行头段键)；行头段键=整词面 + 首段（含剥括号变体）——主导词注册时头段优先于
    逗号尾段（如 连接不正，骨折 的尾段 骨折 不得抢在 骨折(…) 伞行前面）。
    额外把段内嵌的尾部编码剥掉（线上靠 RecallLeads 模糊兜底，导出按查词典语义精确建键）。"""
    keys: set[str] = set()
    head_keys: set[str] = set()

    def add(value: str, head: bool):
        v = NEC_RE.sub(' ', value).strip()
        v = TRAILING_CODE_RE.sub('', v).strip()
        k = norm(v)
        if k:
            keys.add(k)
            if head:
                head_keys.add(k)

    no_brack = SQUARE_RE.sub('', text)
    no_brack = DIRECTIVE_PAREN_RE.sub('', no_brack)
    no_brack = MORPH_PAREN_RE.sub('', no_brack)
    add(no_brack.strip(), True)
    segs = [s.strip() for s in SPLIT_RE.split(no_brack.strip()) if s.strip()]
    for i, seg in enumerate(segs):
        add(seg, i == 0)
        stripped = PAREN_RE.sub('', seg).strip()
        if stripped and stripped != seg:
            add(stripped, i == 0)
        # 或 切分会把括号切开（心肌(急性或伴有…→段 心肌(急性），剥括号变体失效；
        # 编码员按首个括号前的词头查词（心肌/眼/肺——单字词头同权），补该词头键
        if '(' in seg:
            word_head = seg.split('(', 1)[0].strip()
            if norm(word_head):
                add(word_head, i == 0)
            # 词头+单个括号内容的合并变体：肾病(变) 要能按 肾病变 查到（可选后缀括号
            # 是词面的一部分，只产剥括号键会丢 肾病变/冠状动脉 这类合成读法）
            for g in PAREN_RE.findall(seg):
                gc = g[1:-1].strip()
                if gc and word_head and len(norm(word_head + gc)) >= 2:
                    add(word_head + gc, i == 0)
    return keys, head_keys


class Node:
    __slots__ = ('seq', 'level', 'parent', 'raw', 'name', 'code', 'lws', 'enabled',
                 'kind', 'target', 'children', 'keys', 'keys_head', 'full', 'en')

    def __init__(self, seq, level, parent, raw, name, code, lws, enabled):
        self.seq, self.level, self.parent = seq, level, parent
        self.raw, self.name, self.code, self.lws, self.enabled = raw, name, code, lws, enabled
        self.kind, self.target = '无', None
        self.children = []
        self.keys: set[str] = set()
        self.keys_head: set[str] = set()
        self.full, self.en = '', ''


def parse_pointer(text: str):
    """返回 (kind, target, 剩余词面)。与 ParseNode 顺序一致：现译名 > 横杠见/另见 > 括号另见。
    先剥行首层级横杠（ParseNode 同款 TrimStart）：否则 `－见于(由于)` 会被 `－见` 误判成
    见指针（目标=于…）——剥掉横杠后「见」前无横杠/空白，正则不命中，见于 是正常词面。"""
    text = text.strip().lstrip(DASHES).strip()
    m = REDIRECT_RE.search(text)
    if m:
        return '现译名', m.group(1).strip(), text[:m.start()].strip(' ' + DASHES).strip()
    m = JUMP_RE.search(text)
    if m:
        kind = '另见' if m.group(1) == '另见' else '见'
        return kind, m.group(2).strip(), text[:m.start()].strip(' ' + DASHES).strip()
    m = PAREN_JUMP_RE.search(text)
    if m:
        return '另见', m.group(1).strip(), text
    return '无', None, text


rows = []
with open(SRC, encoding='utf-8') as f:
    for line in f:
        parts = line.rstrip('\n').split('\t')
        if len(parts) < 8:
            continue
        seq, level, parent, raw, name, code, lws, enabled = parts[:8]
        rows.append(Node(int(seq), int(level), int(parent), raw.strip(), name.strip(),
                         code.strip(), int(lws), enabled.strip()))

by_seq = {n.seq: n for n in rows}
try:
    with open(EN_SRC, encoding='utf-8') as f:
        for line in f:
            parts = line.rstrip('\n').split('\t')
            if len(parts) >= 2 and parts[0].isdigit() and int(parts[0]) in by_seq:
                by_seq[int(parts[0])].en = parts[1].strip()
except FileNotFoundError:
    pass
for n in rows:
    # 线上 ParseNode 读的是 index_name（清洗过行尾编码），指针解析必须同源；
    # raw_name 仅作展示列。
    kind, target, rest = parse_pointer(n.name)
    n.kind, n.target = kind, target
    n.keys, n.keys_head = alias_keys(rest)
    if n.parent in by_seq and by_seq[n.parent] is not n:
        by_seq[n.parent].children.append(n)

# 完整索引名称 = 字典式 $ 连接的全层级路径（根主导词 → 本行）；
# 每级取剥指针/剥行首横杠后的词面（与线上匹配读的 index_name 同源）。
for n in rows:
    parts = []
    cur: Node | None = n
    while cur is not None:
        _, _, rest = parse_pointer(cur.name)
        rest = PAREN_JUMP_RE.sub('', rest).strip(' ' + DASHES).strip()
        parts.append(rest)
        cur = by_seq.get(cur.parent) if cur.parent in by_seq and by_seq[cur.parent] is not cur else None
    n.full = '$'.join(reversed(parts))

# 主导词表：0 级且非 见/现译名 指针行（IsValidLead），两层注册：
# 行头段键（词头，含剥括号变体）优先；逗号尾段键（如 连接不正，骨折 的 骨折、
# 不随意运动，异常的 的 异常的）只补空位——单层 first-wins 会让尾段键抢在
# 专用主导词行（骨折 伞行 / 异常的 - 另见 异常）前面（线上 LeadByExactKey 同款碰撞缺陷）。
lead_by_head: dict[str, list[Node]] = {}
lead_by_tail: dict[str, list[Node]] = {}
for n in rows:
    if n.level == 0 and n.kind not in ('见', '现译名'):
        for k in n.keys_head:
            lead_by_head.setdefault(k, []).append(n)
        for k in n.keys - n.keys_head:
            lead_by_tail.setdefault(k, []).append(n)


def lookup_leads(key: str) -> list[Node]:
    """同名键可能命中多个主导词行（闭塞(管腔) 与 闭塞，闭合 都有键 闭塞）——
    编码员翻词典会把几条都翻一遍，任一条子树能接住修饰词就算找到。头段层在前。"""
    seen, out = set(), []
    for ld in lead_by_head.get(key, []) + lead_by_tail.get(key, []):
        if ld.seq not in seen:
            seen.add(ld.seq)
            out.append(ld)
    return out


def resolve_leads(target: str) -> list[Node]:
    """查词典口径（用户拍板）：切段不设 <2 字门槛——单字主导词（癌/疝/瘘）也要认；
    情况→疾病映射保持。返回候选主导词列表（可能多条）。"""
    raw_segs = []
    for s in SPLIT_RE.split(target):
        k = norm(SQUARE_RE.sub('', s))
        if k:
            raw_segs.append(s.strip())
    if not raw_segs:
        return []
    first = norm(SQUARE_RE.sub('', raw_segs[0]))
    if first == '情况':
        first = '疾病'
    leads = lookup_leads(first)
    if leads:
        return leads
    concat = ''.join('疾病' if norm(SQUARE_RE.sub('', s)) == '情况' else norm(SQUARE_RE.sub('', s))
                     for s in raw_segs)
    return lookup_leads(concat)


# 0 级 见/现译名 转指行单独建键（它们不是主导词，但编码员翻词典会翻到、并顺势跟跳）
ptr_by_head: dict[str, list[Node]] = {}
ptr_by_tail: dict[str, list[Node]] = {}
for n in rows:
    if n.level == 0 and n.kind in ('见', '现译名'):
        for k in n.keys_head:
            ptr_by_head.setdefault(k, []).append(n)
        for k in n.keys - n.keys_head:
            ptr_by_tail.setdefault(k, []).append(n)


def resolve_leads_deep(target: str, hops: int = 0, visited: frozenset = frozenset()) -> list[Node]:
    """正常主导词查不到时，沿 0 级 见/现译名 转指行跟跳（机能障碍 - 见 功能障碍 →
    机能障碍 可达 功能障碍），最多 3 跳防环。返回最终可达的主导词候选（可能为空）。"""
    leads = resolve_leads(target)
    if leads or hops >= 3:
        return leads
    segs = [s.strip() for s in SPLIT_RE.split(target) if norm(SQUARE_RE.sub('', s))]
    if not segs:
        return []
    first = norm(SQUARE_RE.sub('', segs[0]))
    if first == '情况':
        first = '疾病'
    concat = ''.join('疾病' if norm(SQUARE_RE.sub('', s)) == '情况' else norm(SQUARE_RE.sub('', s))
                     for s in segs)
    seen, out = set(), []
    for ptr in ptr_by_head.get(first, []) + ptr_by_tail.get(first, []) + ptr_by_head.get(concat, []):
        if ptr.seq in seen or ptr.seq in visited or not ptr.target:
            continue
        seen.add(ptr.seq)
        nxt = TAIL_ANOTHER_RE.sub('', ptr.target).strip()
        if not nxt or CODE_TOKEN_RE.search(nxt):
            continue
        out.extend(resolve_leads_deep(nxt, hops + 1, visited | {ptr.seq}))
    return out


def is_ancestor_or_self(anc: Node, node: Node) -> bool:
    cur = node
    seen = 0
    while cur is not None and seen < 64:
        if cur.seq == anc.seq:
            return True
        cur = by_seq.get(cur.parent)
        seen += 1
    return False


reach_cache: dict[tuple[int, frozenset], bool] = {}


def reachable(lead: Node, mkeys: frozenset):
    key = (lead.seq, mkeys)
    if key in reach_cache:
        return reach_cache[key]
    hit = None
    q = deque([lead])
    while q:
        cur = q.popleft()
        if cur is not lead and cur.keys & mkeys:
            hit = cur
            break
        q.extend(cur.children)
    reach_cache[key] = hit
    return hit


path_pair_cache: dict[tuple[int, str, str], bool] = {}


def path_pair_reachable(lead: Node, k1: str, k2: str) -> bool:
    """修饰词拆两半后的路径读法：两半各自精确命中某行别名键，且出现在同一条
    lead→叶 祖先链上（一半是另一半的祖先）——如 乙型 写成 －型→－－乙，编码员
    翻到「型」再翻到「乙」。顺序不限（库里限定词常倒装）。"""
    key = (lead.seq, k1, k2)
    if key in path_pair_cache:
        return path_pair_cache[key]
    hit = False
    q = deque((c, lead.keys) for c in lead.children)
    while q:
        cur, anc = q.popleft()
        if (k1 in cur.keys and k2 in anc) or (k2 in cur.keys and k1 in anc):
            hit = True
            break
        nanc = anc | cur.keys
        q.extend((ch, nanc) for ch in cur.children)
    path_pair_cache[key] = hit
    return hit


def _is_subseq(a: str, b: str) -> bool:
    it = iter(b)
    return all(ch in it for ch in a)


subseq_cache: dict[tuple[int, str], bool] = {}


def subseq_reachable(lead: Node, mk: str):
    """最终兜底：修饰词作为子序列命中子树内某行的「短键」，键最多比修饰词多 1 字——
    鼓室(膜)(…) 的键 鼓室膜 接得住 鼓膜，扭转(性) 接得住 扭转（编码员翻词典的近形读法）；
    键长上限挡掉 按部位⊆按部位编码 这类指令行误吞。返回命中的键（无则 None）。"""
    key = (lead.seq, mk)
    if key in subseq_cache:
        return subseq_cache[key]
    hit = None
    q = deque([lead])
    while q:
        cur = q.popleft()
        if cur is not lead:
            for k in cur.keys:
                if len(k) <= len(mk) + 1 and _is_subseq(mk, k):
                    hit = k
                    break
            if hit:
                break
        q.extend(cur.children)
    subseq_cache[key] = hit
    return hit


# ---- 肿瘤索引库兜底：ICD 判否且目标首段是 肿瘤 时，修饰段转肿瘤表查可达（用户 11-02 拍板）----
TUMOR_SRC = 'logs/_tumor_full.tsv'


class TumorNode:
    __slots__ = ('seq', 'children', 'keys')

    def __init__(self, seq):
        self.seq = seq
        self.children = []
        self.keys: set[str] = set()


tumor_by_seq: dict[int, TumorNode] = {}
try:
    with open(TUMOR_SRC, encoding='utf-8') as f:
        for line in f:
            parts = line.rstrip('\n').split('\t')
            if len(parts) < 4:
                continue
            seq, level, parent, name = parts[:4]
            tumor_by_seq[int(seq)] = TumorNode(int(seq))
    parents: set[int] = set()
    child_parent: dict[int, int] = {}
    for line in open(TUMOR_SRC, encoding='utf-8'):
        parts = line.rstrip('\n').split('\t')
        if len(parts) < 4:
            continue
        seq, level, parent, name = int(parts[0]), int(parts[1]), int(parts[2]), parts[3]
        node = tumor_by_seq[seq]
        _, _, rest = parse_pointer(name)
        node.keys = alias_keys(rest)[0]
        child_parent[seq] = parent
        if parent in tumor_by_seq and tumor_by_seq[parent] is not node:
            tumor_by_seq[parent].children.append(node)
            parents.add(parent)
    tumor_roots = [n for s, n in tumor_by_seq.items()
                   if child_parent.get(s, 0) not in tumor_by_seq]
except FileNotFoundError:
    tumor_roots = []

tumor_cache: dict[frozenset, bool] = {}


def tumor_reachable(mkeys: frozenset) -> bool:
    if not tumor_roots:
        return False
    if mkeys in tumor_cache:
        return tumor_cache[mkeys]
    hit = False
    q = deque(tumor_roots)
    seen = set()
    while q:
        cur = q.popleft()
        if cur.seq in seen:
            continue
        seen.add(cur.seq)
        if cur.keys & mkeys:
            hit = True
            break
        q.extend(cur.children)
    tumor_cache[mkeys] = hit
    return hit


def tumor_fallback(mods: list[str]):
    """肿瘤族指针的肿瘤索引库兜底：非行为词修饰段在肿瘤表树内可达 → 是。"""
    checked, missing = [], []
    for m in mods:
        mk = norm(SQUARE_RE.sub('', m))
        if len(mk) < 2 or mk in BEHAVIOR:
            continue
        checked.append(mk)
        if not tumor_reachable(frozenset(alias_keys(m)[0])):
            missing.append(mk)
    if not checked:
        return '是', '肿瘤索引库兜底（行为词由编码列承载）'
    if not missing:
        return '是', '肿瘤索引库兜底可达：' + '、'.join(checked)
    return '否', 'ICD索引与肿瘤索引库均不可达修饰词：' + '、'.join(missing)


detail, reasons = [], Counter()


def judge(lead: Node, segs: list[str], n: Node):
    evidence, missing = [], []
    for m in segs[1:]:
        mk = norm(SQUARE_RE.sub('', m))
        if len(mk) < 2:
            # 单字修饰段（颈/肺/脑/与…）不做可达性检查：索引子行不会以裸单字当别名键，
            # 严格匹配只会误伤（线上 ResolveJumpLead 同款丢弃）。
            continue
        mkeys = frozenset(alias_keys(m)[0])
        # 修饰词可命中主导词自身标题（伤口，开放性(动物咬伤)… 的 开放性 就写在主导词词面里），
        # 再搜子树；都落空才拆两半走路径读法（乙型 = －型→－－乙）；别名变体也试
        if mkeys & lead.keys:
            evidence.append(f'{mk}→主导词词面')
            continue
        node = reachable(lead, mkeys)
        if node is not None:
            evidence.append(f'{mk}→{node.name}')
            continue
        ok = False
        for cand in mkeys | {mk}:
            for i in range(1, len(cand)):
                if path_pair_reachable(lead, cand[:i], cand[i:]):
                    evidence.append(f'{mk}→路径 {cand[:i]}/{cand[i:]}')
                    ok = True
                    break
            if ok:
                break
        # 仍不可达 → 子序列短键兜底（鼓膜 ⊆ 鼓室膜，编码员见 鼓室(膜) 即知是 鼓膜）
        if not ok:
            for cand in mkeys | {mk}:
                hit_key = subseq_reachable(lead, cand)
                if hit_key:
                    evidence.append(f'{mk}≈{hit_key}（近形）')
                    ok = True
                    break
        if not ok:
            missing.append(mk or m)
    if missing:
        if segs and norm(SQUARE_RE.sub('', segs[0])) in TUMOR_TRIGGER_HEADS:
            # 用户 11-02 拍板：肿瘤族指针在 ICD 索引找不到时，转肿瘤索引库查修饰词
            return tumor_fallback(segs[1:])
        return '否', f'主导词[{norm(lead.raw)[:20]}]下不可达修饰词：{"、".join(missing)}'
    if n.code == '' and not n.children and is_ancestor_or_self(lead, n):
        # 自环死指针（纪律12/19），例外（用户 11-02/11-03 拍板）：任一修饰段（不限字数）
        # 命中主导词直接子层 → 编码员翻到主导词第一层即取到码 → 是
        # （25123 囊肿，脑 / 2511 并发症，接种 / 43002 效应，有害，高海拔——43002 的
        # 有害 在主导词词面、高海拔 是直接子行；13435 骨折，脊椎 只在深层 → 维持否）
        child_keys: set[str] = set()
        for c in lead.children:
            child_keys |= c.keys
        branch = []
        for m in segs[1:]:
            if frozenset(alias_keys(m)[0]) & child_keys:
                branch.append(norm(SQUARE_RE.sub('', m)) or m)
        if branch:
            return '是', '自环但目标分支在主导词直接子层（' + '、'.join(branch) + '）'
        return '否', '死指针自环（解析回自身/祖先且本行无码无子行）'
    return '是', '主导词[' + lead.name + ']' + ('；' + '；'.join(evidence) if evidence else '')


for n in rows:
    if n.kind == '无':
        visible, why = '', ''
    else:
        target = TAIL_ANOTHER_RE.sub('', n.target or '').strip()
        if not target:
            visible, why = '否', '目标为空'
        elif CODE_TOKEN_RE.search(target):
            visible, why = '是', '目标直接给出编码'
        elif (norm(target) in GENERIC
              or (SPLIT_RE.split(target) and norm(SPLIT_RE.split(target)[0]) in GENERIC)) \
                and len([s for s in SPLIT_RE.split(target) if norm(SQUARE_RE.sub('', s))]) <= 1:
            visible, why = '是', '泛词兜底（病，疾病 主导词在库）'
        else:
            leads = resolve_leads_deep(target)
            if not leads:
                first_key = norm(SQUARE_RE.sub('', next(
                    (s for s in SPLIT_RE.split(target) if norm(SQUARE_RE.sub('', s))), '')))
                if first_key in TUMOR_TRIGGER_HEADS:
                    mods = [s.strip() for s in SPLIT_RE.split(target)[1:] if norm(SQUARE_RE.sub('', s))]
                    visible, why = tumor_fallback(mods)
                else:
                    visible, why = '否', f'无此主导词（目标={target}）'
            else:
                raw_segs = [s.strip() for s in SPLIT_RE.split(target) if norm(SQUARE_RE.sub('', s))]
                # 每条候选主导词都判一遍：任一条给出 是 就按词典"翻到了"定是；
                # 全否时取第一条候选的否因（first-wins 口径）作判定说明
                verdicts = []
                for ld in leads:
                    v, w = judge(ld, raw_segs, n)
                    verdicts.append((v, w))
                    if v == '是':
                        break
                visible, why = next(((v, w) for v, w in verdicts if v == '是'), verdicts[0])
        reasons[why.split('（')[0] if visible == '否' else ''] += 1
    detail.append((n, visible, why))

# 简化统计
stat = Counter()
for d, v, w in detail:
    stat[(d.kind, v or '—')] += 1

wb = openpyxl.Workbook()
ws = wb.active
ws.title = '明细'
head = ['seq_no', '层级', '父行seq', '索引名称(raw_name)', '完整索引名称', '英文索引名称', '索引编码', '主导词标记', '是否启用',
        '指针类型', '指针目标', '是否可见', '判定说明']
ws.append(head)
for c in range(1, len(head) + 1):
    ws.cell(1, c).font = Font(bold=True)
    ws.cell(1, c).fill = PatternFill('solid', fgColor='D9E1F2')
for n, v, w in detail:
    ws.append([n.seq, n.level, n.parent, n.raw, n.full, n.en, n.code, n.lws,
               '是' if n.enabled == '1' else '否', n.kind, n.target if n.kind != '无' else '', v, w])
for col, width in zip('ABCDEFGHIJKLM', (8, 6, 8, 80, 90, 50, 14, 10, 8, 8, 30, 8, 60)):
    ws.column_dimensions[col].width = width
ws.freeze_panes = 'A2'
ws.auto_filter.ref = f'A1:M{ws.max_row}'

ws2 = wb.create_sheet('汇总')
ws2.append(['指针类型', '是否可见', '行数'])
for (kind, v), c in sorted(stat.items()):
    ws2.append([kind, v, c])
ws2.append([])
ws2.append(['不可见原因分布（仅指针行）'])
for why, c in reasons.most_common(30):
    if why:
        ws2.append([why, c])
ws2.column_dimensions['A'].width = 50
ws2.column_dimensions['B'].width = 12
ws2.column_dimensions['C'].width = 10

ws3 = wb.create_sheet('口径说明')
notes = [
    '1. 指针识别与 IcdIndexModels.ParseNode 同款：先剥行首层级横杠，再认 `见/另见 X`（横杠形态）、结尾圆括号 `(另见 X)`（括号只认另见，避免 (见于) 误判）、`现译名 X`；「见于/见于(由于)」是正常词面不是指针。',
    '2. 主导词表 = 0 级且非 见/现译名 行（IsValidLead 口径），键为词面与 或/，、 切段的归一化形式（归一=去标点空白/小写/罗马数字转阿拉伯）。',
    '3. 可见=是：目标直接给出编码；或目标是单段泛词 情况/疾病/病（末位兜底通道，病，疾病 主导词在库；带修饰段的泛词目标按正常主导词判定，用户 11-02 拍板：病 下找不到 按病毒类型 这类修饰段不走兜底）；或目标首段解析到主导词且其余修饰段在该主导词子树内可达。',
    '4. 可见=否：目标解析不到主导词；或修饰段在主导词子树不可达（用户口径：如 见 骨折，脊椎 而主导词 骨折 下找不到 脊椎）；或自环死指针（解析回自身/祖先且本行无码无子行，纪律12/19）。',
    '5. 与线上匹配器的差异：ResolveJumpLead 的召回模糊兜底（RecallLeads top-1）未实现——模糊才能救回的目标一律记 否；目标修饰段与索引行走同一套 alias_keys 建键（剥方括号/或，、切段/剥尾码）；切段不设 <2 字门槛（用户拍板：单字主导词如 癌/疝/瘘 也要认，线上 ResolveJumpLead 会丢 <2 字段）。',
    '6. 指针目标里的 又称 尾巴先剥掉再判定（TailAnotherNameRegex 同款）。',
    '7. 肿瘤族兜底（用户 11-02 拍板）：ICD 索引判否且目标首段是 肿瘤 时，修饰段转肿瘤索引库（tumor_index，单根 肿瘤，肿瘤性 → 部位子行）查可达；行为词（恶性/良性/原位/动态未定/行为未定）由肿瘤表编码列承载不参与表内查找，其余修饰段全部可达 → 是（如 阿斯金瘤 见 肿瘤，结缔组织，恶性 → 表内 结缔组织 NEC 行命中）。仍有缺失 → 否（ICD索引与肿瘤索引库均不可达）。',
    '8. 完整索引名称列 = 字典式 $ 连接的全层级路径（根主导词 → 本行，每级取剥行尾编码/行首横杠/指针后的词面）；索引名称(raw_name) 是带行尾编码与指针的本行原始词面。英文索引名称列 = icd_index.raw_name_en（索引原始英文名称，库内为空则留空）。',
    '9. 是否可见=是 的判定说明给出证据链：主导词[词面]；修饰词→命中的子行（无修饰段时只列主导词）。特殊形态：目标直接给出编码 / 泛词兜底 / 自环但目标分支在主导词直接子层 / 肿瘤索引库兜底，说明文字各自标注；「≈（近形）」表示子序列短键兜底（如 鼓膜≈鼓室膜）、「路径 a/b」表示词形拆在父子两层（如 乙型=型→乙）。',
]
for i, t in enumerate(notes, 1):
    ws3.cell(i, 1, t).alignment = openpyxl.styles.Alignment(wrap_text=True, vertical='top')
ws3.column_dimensions['A'].width = 120

try:
    wb.save(OUT)
    print(f'saved {OUT}')
except PermissionError:
    OUT = OUT.replace('.xlsx', '-v2.xlsx')
    wb.save(OUT)
    print(f'原文件被占用（请关闭 Excel），已另存 {OUT}')
print('stat:', dict(stat))
print('reasons:', dict(reasons.most_common(10)))

# 抽查锚点行
for n, v, w in detail:
    if ('骨折' in n.raw and '脊柱' in n.raw and n.kind == '见') or ('肿瘤(形成)' in n.raw) \
            or ('小脑脑桥' in n.raw) or ('阿斯金' in n.raw) or ('异常所见' in n.raw and n.level == 1 and n.seq == 39):
        print('CHECK', n.seq, n.kind, n.target, '=>', v, w)
