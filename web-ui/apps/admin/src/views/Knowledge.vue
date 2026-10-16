<script setup lang="ts">
import {
  BookOpen,
  CheckCircle2,
  ChevronDown,
  ChevronLeft,
  ChevronRight,
  Database,
  Download,
  FileJson,
  Loader2,
  Plus,
  Search,
  Upload,
  X,
} from 'lucide-vue-next'
import { computed, nextTick, onMounted, onUnmounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, knowledgeApi } from '@/api'
import type {
  CodeSystemDto,
  Cm3IndexDto,
  CodingRuleDto,
  IcdIndexDto,
  IcdTermSynonymGroupDto,
  ImportCodeSystemResponse,
  ImportCodingRulesResponse,
  MedicalCodeDto,
} from '@/api'
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Input,
  Label,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Separator,
  Switch,
  Tabs,
  TabsContent,
  TabsList,
  TabsTrigger,
  Textarea,
  toast,
} from '@tabtab/ui'

const { t } = useI18n()

/* ============== 顶层 Tab ============== */
const activeMainTab = ref('browse')

/* ============== 数据浏览 — 编码体系 ============== */
const codeSystems = ref<CodeSystemDto[]>([])
const codeSystemsLoading = ref(false)

async function loadCodeSystems() {
  codeSystemsLoading.value = true
  try {
    codeSystems.value = await knowledgeApi.getCodeSystems()
  } catch { toast.error(t('knowledge.loadFailed')) }
  finally { codeSystemsLoading.value = false }
}

/* ============== 数据浏览 — 医学编码 ============== */
const medicalCodes = ref<MedicalCodeDto[]>([])
const medicalCodesTotal = ref(0)
const medicalCodesPage = ref(1)
const medicalCodesPageSize = 20
const medicalCodesLoading = ref(false)
const medicalCodesKeyword = ref('')
const medicalCodesFilter = ref('__ALL__')

const medicalCodesTotalPages = computed(() =>
  Math.max(1, Math.ceil(medicalCodesTotal.value / medicalCodesPageSize)),
)

async function loadMedicalCodes() {
  medicalCodesLoading.value = true
  try {
    const result = await knowledgeApi.getMedicalCodes({
      page: medicalCodesPage.value,
      pageSize: medicalCodesPageSize,
      codeSystem: medicalCodesFilter.value !== '__ALL__' ? medicalCodesFilter.value : undefined,
      keyword: medicalCodesKeyword.value || undefined,
    })
    medicalCodes.value = result.items
    medicalCodesTotal.value = result.total
  } catch { toast.error(t('knowledge.loadFailed')) }
  finally { medicalCodesLoading.value = false }
}

let medicalCodeSearchTimer: ReturnType<typeof setTimeout> | null = null
function onMedicalCodeSearchInput() {
  if (medicalCodeSearchTimer) clearTimeout(medicalCodeSearchTimer)
  medicalCodeSearchTimer = setTimeout(() => {
    medicalCodesPage.value = 1
    loadMedicalCodes()
  }, 400)
}

function onMedicalCodeFilterChange() {
  medicalCodesPage.value = 1
  loadMedicalCodes()
}

/* ============== 数据浏览 — 同义词 ============== */
const synonyms = ref<IcdTermSynonymGroupDto[]>([])
const synonymsTotal = ref(0)
const synonymsPage = ref(1)
const synonymsPageSize = 20
const synonymsLoading = ref(false)
const synonymsKeyword = ref('')

const synonymsTotalPages = computed(() =>
  Math.max(1, Math.ceil(synonymsTotal.value / synonymsPageSize)),
)

const pagedSynonyms = computed(() => {
  const start = (synonymsPage.value - 1) * synonymsPageSize
  return synonyms.value.slice(start, start + synonymsPageSize)
})

function synonymTermTypeLabel(termType: string) {
  return termType === 'PROCEDURE'
    ? t('knowledge.termTypeProcedure')
    : t('knowledge.termTypeDiagnosis')
}

async function loadSynonyms() {
  synonymsLoading.value = true
  try {
    const result = await knowledgeApi.getTermSynonyms({
      keyword: synonymsKeyword.value || undefined,
    })
    synonyms.value = result.items
    synonymsTotal.value = result.total
  } catch { toast.error(t('knowledge.loadFailed')) }
  finally { synonymsLoading.value = false }
}

let synonymSearchTimer: ReturnType<typeof setTimeout> | null = null
function onSynonymSearchInput() {
  if (synonymSearchTimer) clearTimeout(synonymSearchTimer)
  synonymSearchTimer = setTimeout(() => {
    synonymsPage.value = 1
    loadSynonyms()
  }, 400)
}

/* ============== 同义词组增删改（单条记录粒度） ============== */
interface SynonymMemberDraft {
  /** 库内记录 id；为空表示新增的等同词。 */
  id: string | null
  variantTerm: string
  isEnabled: boolean
}

const synonymDialogOpen = ref(false)
const synonymSaving = ref(false)
const synonymDraft = ref<{
  /** 编辑中的组原始通道+规范词，用于判断是否需要逐条 PUT 重指向。 */
  originalTermType: string
  originalCanonical: string
  termType: string
  canonicalTerm: string
  members: SynonymMemberDraft[]
} | null>(null)

function openSynonymCreate() {
  synonymDraft.value = {
    originalTermType: 'DIAGNOSIS',
    originalCanonical: '',
    termType: 'DIAGNOSIS',
    canonicalTerm: '',
    members: [{ id: null, variantTerm: '', isEnabled: true }],
  }
  synonymDialogOpen.value = true
}

function openSynonymEdit(group: IcdTermSynonymGroupDto) {
  synonymDraft.value = {
    originalTermType: group.termType,
    originalCanonical: group.canonicalTerm,
    termType: group.termType,
    canonicalTerm: group.canonicalTerm,
    members: group.variants.map(variant => ({
      id: variant.id,
      variantTerm: variant.variantTerm,
      isEnabled: variant.isEnabled,
    })),
  }
  synonymDialogOpen.value = true
}

function addSynonymMember() {
  synonymDraft.value?.members.push({ id: null, variantTerm: '', isEnabled: true })
}

function removeSynonymMember(index: number) {
  const draft = synonymDraft.value
  if (!draft) return
  draft.members.splice(index, 1)
  if (draft.members.length === 0) {
    draft.members.push({ id: null, variantTerm: '', isEnabled: true })
  }
}

/** 等价类要求组内至少有一个与规范词不同的等同词。 */
function validateSynonymDraft(draft: NonNullable<typeof synonymDraft.value>) {
  const canonical = draft.canonicalTerm.trim()
  const members = draft.members.map(member => member.variantTerm.trim())
  if (!canonical) return t('knowledge.canonicalTermRequired')
  if (members.length === 0 || members.every(member => !member)) {
    return t('knowledge.atLeastOneVariantRequired')
  }
  if (members.some(member => member === canonical)) {
    return t('knowledge.variantEqualsCanonical')
  }
  const distinct = new Set(members.filter(Boolean))
  if (distinct.size !== members.filter(Boolean).length) {
    return t('knowledge.duplicateVariantTerm')
  }
  return ''
}

async function saveSynonymDraft() {
  const draft = synonymDraft.value
  if (!draft) return
  const message = validateSynonymDraft(draft)
  if (message) {
    toast.error(message)
    return
  }

  const canonical = draft.canonicalTerm.trim()
  const keptIds = new Set(
    draft.members
      .filter(member => member.id)
      .map(member => member.id as string),
  )
  synonymSaving.value = true
  try {
    // 1) 组内被移出的等同词逐条删除（一行就是一条等同边）。
    if (draft.originalCanonical) {
      const removed = synonyms.value
        .flatMap(group => group.variants.map(variant => ({ group, variant })))
        .filter(({ group, variant }) =>
          group.termType === draft.originalTermType
          && group.canonicalTerm === draft.originalCanonical
          && !keptIds.has(variant.id))
        .map(({ variant }) => variant.id)
      for (const id of removed) {
        await knowledgeApi.deleteTermSynonym(id)
      }
    }

    // 2) 已有记录就地改词/改组/启停，新记录逐条新增。
    for (const member of draft.members) {
      const variantTerm = member.variantTerm.trim()
      if (!variantTerm) continue
      const payload = {
        termType: draft.termType,
        canonicalTerm: canonical,
        variantTerm,
        isEnabled: member.isEnabled,
      }
      if (member.id) {
        await knowledgeApi.updateTermSynonym(member.id, payload)
      } else {
        await knowledgeApi.createTermSynonym(payload)
      }
    }

    synonymDialogOpen.value = false
    toast.success(t('knowledge.synonymSaved'))
    synonymsPage.value = 1
    loadSynonyms()
  } catch (error) {
    toast.error(error instanceof ApiError ? error.message : t('knowledge.saveFailed'))
  } finally {
    synonymSaving.value = false
  }
}

async function deleteSynonymGroup(group: IcdTermSynonymGroupDto) {
  if (!window.confirm(t('knowledge.deleteGroupConfirm', {
    canonical: group.canonicalTerm,
    count: group.variants.length,
  }))) {
    return
  }

  try {
    for (const variant of group.variants) {
      await knowledgeApi.deleteTermSynonym(variant.id)
    }
    toast.success(t('knowledge.synonymDeleted'))
    synonymsPage.value = 1
    loadSynonyms()
  } catch (error) {
    toast.error(error instanceof ApiError ? error.message : t('knowledge.deleteFailed'))
  }
}

/* ============== 数据浏览 — 编码规则 ============== */
const codingRules = ref<CodingRuleDto[]>([])
const codingRulesTotal = ref(0)
const codingRulesPage = ref(1)
const codingRulesPageSize = 20
const codingRulesLoading = ref(false)
const codingRulesFilter = ref('__ALL__')

const codingRulesTotalPages = computed(() =>
  Math.max(1, Math.ceil(codingRulesTotal.value / codingRulesPageSize)),
)

async function loadCodingRules() {
  codingRulesLoading.value = true
  try {
    const result = await knowledgeApi.getCodingRules({
      page: codingRulesPage.value,
      pageSize: codingRulesPageSize,
      codeSystem: codingRulesFilter.value !== '__ALL__' ? codingRulesFilter.value : undefined,
    })
    codingRules.value = result.items
    codingRulesTotal.value = result.total
  } catch { toast.error(t('knowledge.loadFailed')) }
  finally { codingRulesLoading.value = false }
}

function onCodingRuleFilterChange() {
  codingRulesPage.value = 1
  loadCodingRules()
}

/* ============== 数据浏览 — ICD 索引库（节点树 + 关键词搜索） ============== */
interface IcdIndexTreeNode extends IcdIndexDto {
  hasChildren: boolean
}
interface IcdIndexVisibleRow {
  node: IcdIndexTreeNode
  depth: number
}

const icdIndexKeyword = ref('')
const icdIndexStatusFilter = ref('__ALL__')
const icdIndexIsSearch = computed(() => icdIndexKeyword.value.trim() !== '')

/* 表格固定为视口剩余高度，只在表格容器内滚动，避免外层页面出现滚动条 */
const icdIndexTableWrap = ref<HTMLElement | null>(null)
const icdIndexTableHeight = ref(480)

async function updateIcdIndexTableHeight() {
  const el = icdIndexTableWrap.value
  if (!el) return
  let panel: HTMLElement | null = el.parentElement
  while (panel && !/(auto|scroll)/.test(getComputedStyle(panel).overflowY))
    panel = panel.parentElement
  panel?.scrollTo({ top: 0 })
  // ICD 卡片带负下外边距抵掉面板下边距与页面内边距，外层为固定高度 flex 滚动区，
  // 因此直接把表格增高到卡片底缘距视口底约 10px，超高部分只会在面板内滚动
  const card = el.parentElement?.parentElement?.parentElement ?? null
  for (let i = 0; i < 4; i++) {
    const rect = (card ?? el).getBoundingClientRect()
    const gap = window.innerHeight - rect.bottom
    if (gap > 12) {
      icdIndexTableHeight.value += Math.floor(gap - 10)
      await nextTick()
    } else if (gap < 6) {
      icdIndexTableHeight.value = Math.max(120, icdIndexTableHeight.value + Math.ceil(gap - 10))
      await nextTick()
    } else {
      break
    }
  }
}

watch(icdIndexTableWrap, () => {
  nextTick(updateIcdIndexTableHeight)
  // 数据渲染完成后浏览器可能仍在稳定布局，延时再量一次避免高度偏小
  setTimeout(updateIcdIndexTableHeight, 150)
})

function icdIndexStatusParam() {
  return icdIndexStatusFilter.value !== '__ALL__' ? Number(icdIndexStatusFilter.value) : undefined
}

/* 树模式：根节点分页加载，子节点按父序号懒加载 */
const icdIndexTreeLoading = ref(false)
const icdIndexMoreLoading = ref(false)
const icdIndexRoots = ref<IcdIndexTreeNode[]>([])
const icdIndexRootTotal = ref(0)
const icdIndexRootPageSize = 200
const icdIndexChildrenMap = ref<Record<number, IcdIndexTreeNode[]>>({})
const icdIndexExpanded = ref<Record<number, boolean>>({})
const icdIndexNodeLoading = ref<Record<number, boolean>>({})

const icdIndexVisibleRows = computed<IcdIndexVisibleRow[]>(() => {
  const rows: IcdIndexVisibleRow[] = []
  const walk = (nodes: IcdIndexTreeNode[], depth: number) => {
    for (const node of nodes) {
      rows.push({ node, depth })
      if (icdIndexExpanded.value[node.seqNo])
        walk(icdIndexChildrenMap.value[node.seqNo] ?? [], depth + 1)
    }
  }
  walk(icdIndexRoots.value, 0)
  return rows
})

async function loadIcdIndexTree() {
  icdIndexTreeLoading.value = true
  try {
    const result = await knowledgeApi.getIcdIndexChildren({
      parentSeqNo: 0,
      leadWordStatus: icdIndexStatusParam(),
      offset: 0,
      limit: icdIndexRootPageSize,
    })
    icdIndexRoots.value = result.items
    icdIndexRootTotal.value = result.total
    icdIndexChildrenMap.value = {}
    icdIndexExpanded.value = {}
    icdIndexNodeLoading.value = {}
  } catch { toast.error(t('knowledge.loadFailed')) }
  finally { icdIndexTreeLoading.value = false }
}

async function loadMoreIcdIndexRoots() {
  if (icdIndexMoreLoading.value) return
  icdIndexMoreLoading.value = true
  try {
    const result = await knowledgeApi.getIcdIndexChildren({
      parentSeqNo: 0,
      leadWordStatus: icdIndexStatusParam(),
      offset: icdIndexRoots.value.length,
      limit: icdIndexRootPageSize,
    })
    icdIndexRoots.value = [...icdIndexRoots.value, ...result.items]
    icdIndexRootTotal.value = result.total
  } catch { toast.error(t('knowledge.loadFailed')) }
  finally { icdIndexMoreLoading.value = false }
}

async function toggleIcdIndexNode(node: IcdIndexTreeNode) {
  if (icdIndexExpanded.value[node.seqNo]) {
    icdIndexExpanded.value = { ...icdIndexExpanded.value, [node.seqNo]: false }
    return
  }
  if (!icdIndexChildrenMap.value[node.seqNo]) {
    icdIndexNodeLoading.value = { ...icdIndexNodeLoading.value, [node.seqNo]: true }
    try {
      const result = await knowledgeApi.getIcdIndexChildren({
        parentSeqNo: node.seqNo,
        leadWordStatus: icdIndexStatusParam(),
        limit: 1000,
      })
      icdIndexChildrenMap.value = { ...icdIndexChildrenMap.value, [node.seqNo]: result.items }
    } catch { toast.error(t('knowledge.loadFailed')) }
    finally {
      const next = { ...icdIndexNodeLoading.value }
      delete next[node.seqNo]
      icdIndexNodeLoading.value = next
    }
  }
  icdIndexExpanded.value = { ...icdIndexExpanded.value, [node.seqNo]: true }
}

/* 搜索模式：关键词命中时按平铺分页结果展示 */
const icdIndex = ref<IcdIndexDto[]>([])
const icdIndexTotal = ref(0)
const icdIndexPage = ref(1)
const icdIndexPageSize = 20
const icdIndexLoading = ref(false)

const icdIndexTotalPages = computed(() =>
  Math.max(1, Math.ceil(icdIndexTotal.value / icdIndexPageSize)),
)

async function loadIcdIndexSearch() {
  icdIndexLoading.value = true
  try {
    const result = await knowledgeApi.getIcdIndex({
      page: icdIndexPage.value,
      pageSize: icdIndexPageSize,
      keyword: icdIndexKeyword.value.trim(),
      leadWordStatus: icdIndexStatusParam(),
    })
    icdIndex.value = result.items
    icdIndexTotal.value = result.total
  } catch { toast.error(t('knowledge.loadFailed')) }
  finally { icdIndexLoading.value = false }
}

let icdIndexSearchTimer: ReturnType<typeof setTimeout> | null = null
function onIcdIndexSearchInput() {
  if (icdIndexSearchTimer) clearTimeout(icdIndexSearchTimer)
  icdIndexSearchTimer = setTimeout(() => {
    if (icdIndexIsSearch.value) {
      icdIndexPage.value = 1
      loadIcdIndexSearch()
    } else {
      icdIndex.value = []
    }
  }, 400)
}

function onIcdIndexStatusFilterChange() {
  if (icdIndexIsSearch.value) {
    icdIndexPage.value = 1
    loadIcdIndexSearch()
  } else {
    loadIcdIndexTree()
  }
}

/* ============== 数据浏览 — CM3 索引库（节点树 + 关键词搜索） ============== */
interface Cm3IndexTreeNode extends Cm3IndexDto {
  hasChildren: boolean
}
interface Cm3IndexVisibleRow {
  node: Cm3IndexTreeNode
  depth: number
}

const cm3IndexKeyword = ref('')
const cm3IndexStatusFilter = ref('__ALL__')
const cm3IndexIsSearch = computed(() => cm3IndexKeyword.value.trim() !== '')

/* 表格固定为视口剩余高度，只在表格容器内滚动，避免外层页面出现滚动条 */
const cm3IndexTableWrap = ref<HTMLElement | null>(null)
const cm3IndexTableHeight = ref(480)

async function updateCm3IndexTableHeight() {
  const el = cm3IndexTableWrap.value
  if (!el) return
  let panel: HTMLElement | null = el.parentElement
  while (panel && !/(auto|scroll)/.test(getComputedStyle(panel).overflowY))
    panel = panel.parentElement
  panel?.scrollTo({ top: 0 })
  // 同 ICD 索引库：卡片带负下外边距抵掉面板下边距与页面内边距，直接把表格增高到视口底约 10px
  const card = el.parentElement?.parentElement?.parentElement ?? null
  for (let i = 0; i < 4; i++) {
    const rect = (card ?? el).getBoundingClientRect()
    const gap = window.innerHeight - rect.bottom
    if (gap > 12) {
      cm3IndexTableHeight.value += Math.floor(gap - 10)
      await nextTick()
    } else if (gap < 6) {
      cm3IndexTableHeight.value = Math.max(120, cm3IndexTableHeight.value + Math.ceil(gap - 10))
      await nextTick()
    } else {
      break
    }
  }
}

watch(cm3IndexTableWrap, () => {
  nextTick(updateCm3IndexTableHeight)
  setTimeout(updateCm3IndexTableHeight, 150)
})

function cm3IndexStatusParam() {
  return cm3IndexStatusFilter.value !== '__ALL__' ? Number(cm3IndexStatusFilter.value) : undefined
}

/* 树模式：根节点分页加载，子节点按父序号懒加载 */
const cm3IndexTreeLoading = ref(false)
const cm3IndexMoreLoading = ref(false)
const cm3IndexRoots = ref<Cm3IndexTreeNode[]>([])
const cm3IndexRootTotal = ref(0)
const cm3IndexRootPageSize = 200
const cm3IndexChildrenMap = ref<Record<number, Cm3IndexTreeNode[]>>({})
const cm3IndexExpanded = ref<Record<number, boolean>>({})
const cm3IndexNodeLoading = ref<Record<number, boolean>>({})

const cm3IndexVisibleRows = computed<Cm3IndexVisibleRow[]>(() => {
  const rows: Cm3IndexVisibleRow[] = []
  const walk = (nodes: Cm3IndexTreeNode[], depth: number) => {
    for (const node of nodes) {
      rows.push({ node, depth })
      if (cm3IndexExpanded.value[node.seqNo])
        walk(cm3IndexChildrenMap.value[node.seqNo] ?? [], depth + 1)
    }
  }
  walk(cm3IndexRoots.value, 0)
  return rows
})

async function loadCm3IndexTree() {
  cm3IndexTreeLoading.value = true
  try {
    const result = await knowledgeApi.getCm3IndexChildren({
      parentSeqNo: 0,
      leadWordStatus: cm3IndexStatusParam(),
      offset: 0,
      limit: cm3IndexRootPageSize,
    })
    cm3IndexRoots.value = result.items
    cm3IndexRootTotal.value = result.total
    cm3IndexChildrenMap.value = {}
    cm3IndexExpanded.value = {}
    cm3IndexNodeLoading.value = {}
  } catch { toast.error(t('knowledge.loadFailed')) }
  finally { cm3IndexTreeLoading.value = false }
}

async function loadMoreCm3IndexRoots() {
  if (cm3IndexMoreLoading.value) return
  cm3IndexMoreLoading.value = true
  try {
    const result = await knowledgeApi.getCm3IndexChildren({
      parentSeqNo: 0,
      leadWordStatus: cm3IndexStatusParam(),
      offset: cm3IndexRoots.value.length,
      limit: cm3IndexRootPageSize,
    })
    cm3IndexRoots.value = [...cm3IndexRoots.value, ...result.items]
    cm3IndexRootTotal.value = result.total
  } catch { toast.error(t('knowledge.loadFailed')) }
  finally { cm3IndexMoreLoading.value = false }
}

async function toggleCm3IndexNode(node: Cm3IndexTreeNode) {
  if (cm3IndexExpanded.value[node.seqNo]) {
    cm3IndexExpanded.value = { ...cm3IndexExpanded.value, [node.seqNo]: false }
    return
  }
  if (!cm3IndexChildrenMap.value[node.seqNo]) {
    cm3IndexNodeLoading.value = { ...cm3IndexNodeLoading.value, [node.seqNo]: true }
    try {
      const result = await knowledgeApi.getCm3IndexChildren({
        parentSeqNo: node.seqNo,
        leadWordStatus: cm3IndexStatusParam(),
        limit: 1000,
      })
      cm3IndexChildrenMap.value = { ...cm3IndexChildrenMap.value, [node.seqNo]: result.items }
    } catch { toast.error(t('knowledge.loadFailed')) }
    finally {
      const next = { ...cm3IndexNodeLoading.value }
      delete next[node.seqNo]
      cm3IndexNodeLoading.value = next
    }
  }
  cm3IndexExpanded.value = { ...cm3IndexExpanded.value, [node.seqNo]: true }
}

/* 搜索模式：关键词命中时按平铺分页结果展示 */
const cm3Index = ref<Cm3IndexDto[]>([])
const cm3IndexTotal = ref(0)
const cm3IndexPage = ref(1)
const cm3IndexPageSize = 20
const cm3IndexLoading = ref(false)

const cm3IndexTotalPages = computed(() =>
  Math.max(1, Math.ceil(cm3IndexTotal.value / cm3IndexPageSize)),
)

async function loadCm3IndexSearch() {
  cm3IndexLoading.value = true
  try {
    const result = await knowledgeApi.getCm3Index({
      page: cm3IndexPage.value,
      pageSize: cm3IndexPageSize,
      keyword: cm3IndexKeyword.value.trim(),
      leadWordStatus: cm3IndexStatusParam(),
    })
    cm3Index.value = result.items
    cm3IndexTotal.value = result.total
  } catch { toast.error(t('knowledge.loadFailed')) }
  finally { cm3IndexLoading.value = false }
}

let cm3IndexSearchTimer: ReturnType<typeof setTimeout> | null = null
function onCm3IndexSearchInput() {
  if (cm3IndexSearchTimer) clearTimeout(cm3IndexSearchTimer)
  cm3IndexSearchTimer = setTimeout(() => {
    if (cm3IndexIsSearch.value) {
      cm3IndexPage.value = 1
      loadCm3IndexSearch()
    } else {
      cm3Index.value = []
    }
  }, 400)
}

function onCm3IndexStatusFilterChange() {
  if (cm3IndexIsSearch.value) {
    cm3IndexPage.value = 1
    loadCm3IndexSearch()
  } else {
    loadCm3IndexTree()
  }
}

/* ============== 浏览 Tab 切换加载 ============== */
const browseTab = ref('code-systems')

watch(browseTab, (tab) => {
  if (tab === 'code-systems' && codeSystems.value.length === 0) loadCodeSystems()
  if (tab === 'medical-codes' && medicalCodes.value.length === 0) loadMedicalCodes()
  if (tab === 'synonyms' && synonyms.value.length === 0) loadSynonyms()
  if (tab === 'rules' && codingRules.value.length === 0) loadCodingRules()
  if (tab === 'icd-index' && icdIndexRoots.value.length === 0) loadIcdIndexTree()
  if (tab === 'icd-index') nextTick(updateIcdIndexTableHeight)
  if (tab === 'cm3-index' && cm3IndexRoots.value.length === 0) loadCm3IndexTree()
  if (tab === 'cm3-index') nextTick(updateCm3IndexTableHeight)
})

onMounted(() => {
  loadCodeSystems()
  window.addEventListener('resize', updateIcdIndexTableHeight)
  window.addEventListener('resize', updateCm3IndexTableHeight)
})

onUnmounted(() => {
  window.removeEventListener('resize', updateIcdIndexTableHeight)
  window.removeEventListener('resize', updateCm3IndexTableHeight)
})

/* ============== 统计 ============== */
const totalCodes = computed(() => medicalCodesTotal.value)
const totalSynonyms = computed(() => synonymsTotal.value)
const totalRules = computed(() => codingRulesTotal.value)

/* ============== 编码体系导入 ============== */
const codeSystemForm = ref({
  codeSystem: 'ICD-10',
  version: '2024',
})
const codeSystemLoading = ref(false)
const codeSystemResult = ref<ImportCodeSystemResponse | null>(null)

interface MedicalCodeImportItem {
  code: string
  title: string
  codeType: string
  searchText: string | null
  isEnabled: boolean
}

const sampleICD10: MedicalCodeImportItem[] = [
  { code: 'A00', title: '霍乱', codeType: 'ICD-10', searchText: '霍乱 cholera', isEnabled: true },
  { code: 'A00.0', title: '由于霍乱弧菌01生物群古典生物型引起的霍乱', codeType: 'ICD-10', searchText: '霍乱弧菌古典生物型', isEnabled: true },
  { code: 'A00.1', title: '由于霍乱弧菌01生物群埃尔托生物型引起的霍乱', codeType: 'ICD-10', searchText: '霍乱弧菌埃尔托生物型', isEnabled: true },
  { code: 'A00.9', title: '霍乱，未特指', codeType: 'ICD-10', searchText: '霍乱未特指', isEnabled: true },
  { code: 'A01', title: '伤寒和副伤寒', codeType: 'ICD-10', searchText: '伤寒 typhoid 副伤寒', isEnabled: true },
  { code: 'A01.0', title: '伤寒', codeType: 'ICD-10', searchText: '伤寒 typhoid fever', isEnabled: true },
  { code: 'J00', title: '急性鼻咽炎（感冒）', codeType: 'ICD-10', searchText: '感冒 common cold 鼻咽炎', isEnabled: true },
  { code: 'J06', title: '急性上呼吸道感染，多部位和未特指部位的', codeType: 'ICD-10', searchText: '上呼吸道感染 URI', isEnabled: true },
  { code: 'I10', title: '特发性（原发性）高血压', codeType: 'ICD-10', searchText: '高血压 hypertension essential', isEnabled: true },
  { code: 'E11', title: '非胰岛素依赖型糖尿病', codeType: 'ICD-10', searchText: '糖尿病 diabetes type2 NIDDM', isEnabled: true },
]

const sampleICD9CM3: MedicalCodeImportItem[] = [
  { code: '01.1', title: '颅骨和脑的切开术', codeType: 'ICD-9-CM-3', searchText: '颅骨切开术 craniotomy', isEnabled: true },
  { code: '01.2', title: '颅骨切开术和脑切开术', codeType: 'ICD-9-CM-3', searchText: '颅骨切开 craniotomy brain', isEnabled: true },
  { code: '36.1', title: '冠状动脉旁路移植术', codeType: 'ICD-9-CM-3', searchText: '冠脉搭桥 CABG bypass', isEnabled: true },
  { code: '36.11', title: '(主动脉)冠状动脉旁路移植术，一根冠状动脉', codeType: 'ICD-9-CM-3', searchText: '单支冠脉搭桥', isEnabled: true },
  { code: '36.12', title: '(主动脉)冠状动脉旁路移植术，二根冠状动脉', codeType: 'ICD-9-CM-3', searchText: '双支冠脉搭桥', isEnabled: true },
  { code: '47.0', title: '阑尾切除术', codeType: 'ICD-9-CM-3', searchText: '阑尾切除 appendectomy', isEnabled: true },
  { code: '47.01', title: '腹腔镜阑尾切除术', codeType: 'ICD-9-CM-3', searchText: '腹腔镜阑尾切除 laparoscopic appendectomy', isEnabled: true },
]

async function handleImportCodeSystem() {
  if (codeSystemLoading.value) return
  codeSystemLoading.value = true
  codeSystemResult.value = null
  try {
    const codes = codeSystemForm.value.codeSystem === 'ICD-10' ? sampleICD10 : sampleICD9CM3
    const result = await knowledgeApi.importCodeSystem({
      codeSystem: codeSystemForm.value.codeSystem,
      version: codeSystemForm.value.version,
      codes,
    })
    codeSystemResult.value = result
    toast.success(t('knowledge.importSuccess'))
    loadCodeSystems()
  }
  catch (err) {
    toast.error(err instanceof ApiError ? err.message : t('knowledge.importFailed'))
  }
  finally { codeSystemLoading.value = false }
}

/* ============== 规则导入 ============== */
const rulesForm = ref({
  synonymsJson: '',
  rulesJson: '',
})
const rulesLoading = ref(false)
const rulesResult = ref<ImportCodingRulesResponse | null>(null)

interface TermSynonymImportItem {
  term: string
  normalizedTerm: string
  entityType: string
  codeSystemCode?: string
  code?: string
}

interface CodingRuleImportItem {
  ruleCode: string
  codeSystem: string
  codePattern: string
  ruleType: string
  severity: string
  message: string
  isEnabled: boolean
}

const sampleSynonyms: TermSynonymImportItem[] = [
  { term: '感冒', normalizedTerm: '急性鼻咽炎', entityType: 'DISEASE', codeSystemCode: 'ICD-10', code: 'J00' },
  { term: '高血压', normalizedTerm: '特发性高血压', entityType: 'DISEASE', codeSystemCode: 'ICD-10', code: 'I10' },
  { term: '糖尿病', normalizedTerm: '非胰岛素依赖型糖尿病', entityType: 'DISEASE', codeSystemCode: 'ICD-10', code: 'E11' },
  { term: '阑尾炎手术', normalizedTerm: '阑尾切除术', entityType: 'PROCEDURE', codeSystemCode: 'ICD-9-CM-3', code: '47.0' },
  { term: '搭桥手术', normalizedTerm: '冠状动脉旁路移植术', entityType: 'PROCEDURE', codeSystemCode: 'ICD-9-CM-3', code: '36.1' },
]

const sampleRules: CodingRuleImportItem[] = [
  { ruleCode: 'SZ-001', codeSystem: 'ICD-10', codePattern: 'J00-J06', ruleType: 'VALIDATION', severity: 'WARNING', message: '上呼吸道感染编码需确认病程', isEnabled: true },
  { ruleCode: 'SZ-002', codeSystem: 'ICD-10', codePattern: 'I10-I15', ruleType: 'VALIDATION', severity: 'ERROR', message: '高血压编码需区分原发性和继发性', isEnabled: true },
  { ruleCode: 'SZ-003', codeSystem: 'ICD-10', codePattern: 'E10-E14', ruleType: 'VALIDATION', severity: 'WARNING', message: '糖尿病编码需确认分型', isEnabled: true },
]

function loadSampleSynonyms() {
  rulesForm.value.synonymsJson = JSON.stringify(sampleSynonyms, null, 2)
  toast.success(t('knowledge.sampleDataLoaded'))
}

function loadSampleRules() {
  rulesForm.value.rulesJson = JSON.stringify(sampleRules, null, 2)
  toast.success(t('knowledge.sampleDataLoaded'))
}

async function handleImportRules() {
  if (rulesLoading.value) return
  rulesLoading.value = true
  rulesResult.value = null
  try {
    let synonyms: TermSynonymImportItem[] = []
    let rules: CodingRuleImportItem[] = []
    if (rulesForm.value.synonymsJson.trim()) {
      synonyms = JSON.parse(rulesForm.value.synonymsJson)
    }
    if (rulesForm.value.rulesJson.trim()) {
      rules = JSON.parse(rulesForm.value.rulesJson)
    }
    const result = await knowledgeApi.importCodingRules({ synonyms, rules })
    rulesResult.value = result
    toast.success(t('knowledge.rulesImportSuccess'))
    loadSynonyms()
    loadCodingRules()
  }
  catch (err) {
    if (err instanceof ApiError) toast.error(err.message || t('knowledge.rulesImportFailed'))
    else toast.error(t('knowledge.rulesImportFailed'))
  }
  finally { rulesLoading.value = false }
}
</script>

<template>
  <div class="space-y-6">
    <!-- 页面标题（索引库树/搜索模式隐藏，让表格占满可视高度） -->
    <div v-if="!(activeMainTab === 'browse' && (browseTab === 'icd-index' || browseTab === 'cm3-index'))" class="flex items-center gap-4">
      <div class="p-2 rounded-lg bg-emerald-500/10">
        <BookOpen class="h-5 w-5 text-emerald-500" />
      </div>
      <div>
        <h1 class="text-2xl font-bold text-foreground">{{ t('knowledge.title') }}</h1>
        <p class="text-muted-foreground text-sm mt-0.5">{{ t('knowledge.subtitle') }}</p>
      </div>
    </div>

    <Tabs v-model="activeMainTab" class="space-y-6">
      <TabsList>
        <TabsTrigger value="browse">{{ t('knowledge.browseData') }}</TabsTrigger>
        <TabsTrigger value="code-system">{{ t('knowledge.codeSystemImport') }}</TabsTrigger>
        <TabsTrigger value="rules">{{ t('knowledge.rulesImport') }}</TabsTrigger>
      </TabsList>

      <!-- ============ 数据浏览 ============ -->
      <TabsContent value="browse" class="space-y-6">
        <!-- 统计卡片（索引库树模式隐藏，让表格占满可视高度） -->
        <div v-if="browseTab !== 'icd-index' && browseTab !== 'cm3-index'" class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
          <Card class="border-border/40">
            <CardContent class="pt-6 flex items-center gap-3">
              <div class="p-2 rounded-lg bg-blue-500/10">
                <Database class="h-4 w-4 text-blue-500" />
              </div>
              <div>
                <p class="text-xs text-muted-foreground">{{ t('knowledge.codeSystems') }}</p>
                <p class="text-xl font-bold text-foreground">{{ codeSystems.length }}</p>
              </div>
            </CardContent>
          </Card>
          <Card class="border-border/40">
            <CardContent class="pt-6 flex items-center gap-3">
              <div class="p-2 rounded-lg bg-emerald-500/10">
                <BookOpen class="h-4 w-4 text-emerald-500" />
              </div>
              <div>
                <p class="text-xs text-muted-foreground">{{ t('knowledge.totalCodes') }}</p>
                <p class="text-xl font-bold text-foreground">{{ totalCodes.toLocaleString() }}</p>
              </div>
            </CardContent>
          </Card>
          <Card class="border-border/40">
            <CardContent class="pt-6 flex items-center gap-3">
              <div class="p-2 rounded-lg bg-amber-500/10">
                <Search class="h-4 w-4 text-amber-500" />
              </div>
              <div>
                <p class="text-xs text-muted-foreground">{{ t('knowledge.totalSynonyms') }}</p>
                <p class="text-xl font-bold text-foreground">{{ totalSynonyms.toLocaleString() }}</p>
              </div>
            </CardContent>
          </Card>
          <Card class="border-border/40">
            <CardContent class="pt-6 flex items-center gap-3">
              <div class="p-2 rounded-lg bg-purple-500/10">
                <FileJson class="h-4 w-4 text-purple-500" />
              </div>
              <div>
                <p class="text-xs text-muted-foreground">{{ t('knowledge.totalRules') }}</p>
                <p class="text-xl font-bold text-foreground">{{ totalRules.toLocaleString() }}</p>
              </div>
            </CardContent>
          </Card>
        </div>

        <!-- 浏览子 Tab -->
        <Tabs v-model="browseTab" class="space-y-4">
          <TabsList>
            <TabsTrigger value="code-systems">{{ t('knowledge.codeSystems') }}</TabsTrigger>
            <TabsTrigger value="medical-codes">{{ t('knowledge.medicalCodes') }}</TabsTrigger>
            <TabsTrigger value="synonyms">{{ t('knowledge.termSynonyms') }}</TabsTrigger>
            <TabsTrigger value="rules">{{ t('knowledge.codingRulesTab') }}</TabsTrigger>
            <TabsTrigger value="icd-index">{{ t('knowledge.icdIndexTab') }}</TabsTrigger>
            <TabsTrigger value="cm3-index">{{ t('knowledge.cm3IndexTab') }}</TabsTrigger>
          </TabsList>

          <!-- 编码体系列表 -->
          <TabsContent value="code-systems">
            <Card class="border-border/40">
              <CardContent class="pt-6">
                <div v-if="codeSystemsLoading" class="flex items-center justify-center py-12">
                  <Loader2 class="h-5 w-5 animate-spin text-muted-foreground mr-2" />
                  <span class="text-sm text-muted-foreground">{{ t('knowledge.loading') }}</span>
                </div>
                <div v-else-if="codeSystems.length === 0" class="text-center py-12 text-muted-foreground text-sm">
                  {{ t('knowledge.noCodeSystems') }}
                </div>
                <div v-else class="overflow-x-auto">
                  <table class="w-full text-sm">
                    <thead>
                      <tr class="border-b border-border/40">
                        <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.codeColumn') }}</th>
                        <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.nameColumn') }}</th>
                        <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.versionColumn') }}</th>
                        <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.createdAtColumn') }}</th>
                      </tr>
                    </thead>
                    <tbody>
                      <tr v-for="cs in codeSystems" :key="cs.id" class="border-b border-border/20 hover:bg-muted/30 transition-colors">
                        <td class="py-3 px-4 font-mono text-xs">{{ cs.code }}</td>
                        <td class="py-3 px-4">{{ cs.name }}</td>
                        <td class="py-3 px-4">
                          <Badge variant="secondary">{{ cs.version }}</Badge>
                        </td>
                        <td class="py-3 px-4 text-muted-foreground text-xs">{{ new Date(cs.createdAt).toLocaleDateString() }}</td>
                      </tr>
                    </tbody>
                  </table>
                </div>
              </CardContent>
            </Card>
          </TabsContent>

          <!-- 医学编码列表 -->
          <TabsContent value="medical-codes" class="space-y-4">
            <!-- 搜索和筛选 -->
            <div class="flex flex-col sm:flex-row gap-3">
              <div class="relative flex-1">
                <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                <Input
                  v-model="medicalCodesKeyword"
                  :placeholder="t('knowledge.searchPlaceholder')"
                  class="pl-9"
                  @input="onMedicalCodeSearchInput"
                />
              </div>
              <Select v-model="medicalCodesFilter" @update:model-value="onMedicalCodeFilterChange">
                <SelectTrigger class="w-full sm:w-[200px]">
                  <SelectValue :placeholder="t('knowledge.filterByCodeSystem')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__ALL__">{{ t('knowledge.allCodeSystems') }}</SelectItem>
                  <SelectItem v-for="cs in codeSystems" :key="cs.id" :value="cs.code">{{ cs.code }}</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <Card class="border-border/40">
              <CardContent class="pt-6">
                <div v-if="medicalCodesLoading" class="flex items-center justify-center py-12">
                  <Loader2 class="h-5 w-5 animate-spin text-muted-foreground mr-2" />
                  <span class="text-sm text-muted-foreground">{{ t('knowledge.loading') }}</span>
                </div>
                <div v-else class="space-y-4">
                  <div class="overflow-x-auto">
                    <table class="w-full text-sm">
                      <thead>
                        <tr class="border-b border-border/40">
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.codeColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.codeTitle') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.codeSystemColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.searchTextColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.statusColumn') }}</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="mc in medicalCodes" :key="mc.id" class="border-b border-border/20 hover:bg-muted/30 transition-colors">
                          <td class="py-3 px-4 font-mono text-xs">{{ mc.code }}</td>
                          <td class="py-3 px-4 max-w-[300px] truncate" :title="mc.title">{{ mc.title }}</td>
                          <td class="py-3 px-4">
                            <Badge variant="outline">{{ mc.codeSystemCode }}</Badge>
                          </td>
                          <td class="py-3 px-4 text-muted-foreground text-xs max-w-[200px] truncate" :title="mc.searchText || ''">{{ mc.searchText || '-' }}</td>
                          <td class="py-3 px-4">
                            <Badge v-if="mc.isEnabled" variant="default" class="bg-emerald-500/15 text-emerald-700">{{ t('knowledge.enabled') }}</Badge>
                            <Badge v-else variant="secondary">{{ t('knowledge.disabled') }}</Badge>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  <!-- 分页 -->
                  <div class="flex items-center justify-between">
                    <span class="text-sm text-muted-foreground">{{ t('knowledge.totalRecords', { total: medicalCodesTotal }) }}</span>
                    <div class="flex items-center gap-2">
                      <Button variant="outline" size="sm" :disabled="medicalCodesPage <= 1" @click="medicalCodesPage--; loadMedicalCodes()">
                        <ChevronLeft class="h-4 w-4 mr-1" />
                        {{ t('knowledge.previousPage') }}
                      </Button>
                      <span class="text-sm text-muted-foreground">{{ t('knowledge.pagination', { page: medicalCodesPage, totalPages: medicalCodesTotalPages }) }}</span>
                      <Button variant="outline" size="sm" :disabled="medicalCodesPage >= medicalCodesTotalPages" @click="medicalCodesPage++; loadMedicalCodes()">
                        {{ t('knowledge.nextPage') }}
                        <ChevronRight class="h-4 w-4 ml-1" />
                      </Button>
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>
          </TabsContent>

          <!-- 同义词列表 -->
          <TabsContent value="synonyms" class="space-y-4">
            <div class="flex flex-col sm:flex-row sm:items-center gap-3">
              <div class="relative max-w-md flex-1">
                <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                <Input
                  v-model="synonymsKeyword"
                  :placeholder="t('knowledge.searchSynonymPlaceholder')"
                  class="pl-9"
                  @input="onSynonymSearchInput"
                />
              </div>
              <Button size="sm" @click="openSynonymCreate">
                <Plus class="h-4 w-4 mr-1" />
                {{ t('knowledge.createSynonymGroup') }}
              </Button>
            </div>
            <p class="text-xs text-muted-foreground">{{ t('knowledge.equivalenceHint') }}</p>

            <Card class="border-border/40">
              <CardContent class="pt-6">
                <div v-if="synonymsLoading" class="flex items-center justify-center py-12">
                  <Loader2 class="h-5 w-5 animate-spin text-muted-foreground mr-2" />
                  <span class="text-sm text-muted-foreground">{{ t('knowledge.loading') }}</span>
                </div>
                <div v-else class="space-y-4">
                  <div class="overflow-x-auto">
                    <table class="w-full text-sm">
                      <thead>
                        <tr class="border-b border-border/40">
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.equivalenceGroupColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.entityTypeColumn') }}</th>
                          <th class="text-right py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.actionsColumn') }}</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="syn in pagedSynonyms" :key="`${syn.termType}:${syn.canonicalTerm}`" class="border-b border-border/20 hover:bg-muted/30 transition-colors align-top">
                          <td class="py-3 px-4">
                            <div class="flex flex-wrap gap-1.5">
                              <Badge variant="secondary">{{ syn.canonicalTerm }}</Badge>
                              <Badge
                                v-for="variant in syn.variants"
                                :key="variant.id"
                                variant="outline"
                                :class="variant.isEnabled ? '' : 'line-through text-muted-foreground'"
                              >{{ variant.variantTerm }}</Badge>
                            </div>
                          </td>
                          <td class="py-3 px-4">
                            <Badge variant="outline">{{ synonymTermTypeLabel(syn.termType) }}</Badge>
                          </td>
                          <td class="py-3 px-4 text-right whitespace-nowrap">
                            <Button variant="ghost" size="sm" @click="openSynonymEdit(syn)">
                              {{ t('knowledge.edit') }}
                            </Button>
                            <Button variant="ghost" size="sm" class="text-destructive" @click="deleteSynonymGroup(syn)">
                              {{ t('knowledge.delete') }}
                            </Button>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  <div class="flex items-center justify-between">
                    <span class="text-sm text-muted-foreground">{{ t('knowledge.totalRecords', { total: synonymsTotal }) }}</span>
                    <div class="flex items-center gap-2">
                      <Button variant="outline" size="sm" :disabled="synonymsPage <= 1" @click="synonymsPage--; loadSynonyms()">
                        <ChevronLeft class="h-4 w-4 mr-1" />
                        {{ t('knowledge.previousPage') }}
                      </Button>
                      <span class="text-sm text-muted-foreground">{{ t('knowledge.pagination', { page: synonymsPage, totalPages: synonymsTotalPages }) }}</span>
                      <Button variant="outline" size="sm" :disabled="synonymsPage >= synonymsTotalPages" @click="synonymsPage++; loadSynonyms()">
                        {{ t('knowledge.nextPage') }}
                        <ChevronRight class="h-4 w-4 ml-1" />
                      </Button>
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>

            <Dialog v-model:open="synonymDialogOpen">
              <DialogContent class="sm:max-w-lg">
                <DialogHeader>
                  <DialogTitle>{{ synonymDraft?.originalCanonical ? t('knowledge.editSynonymGroup') : t('knowledge.createSynonymGroup') }}</DialogTitle>
                  <DialogDescription>{{ t('knowledge.equivalenceHint') }}</DialogDescription>
                </DialogHeader>
                <div v-if="synonymDraft" class="space-y-4">
                  <div class="grid grid-cols-2 gap-3">
                    <div class="space-y-2">
                      <Label>{{ t('knowledge.entityTypeColumn') }}</Label>
                      <Select v-model="synonymDraft.termType">
                        <SelectTrigger><SelectValue /></SelectTrigger>
                        <SelectContent>
                          <SelectItem value="DIAGNOSIS">{{ t('knowledge.termTypeDiagnosis') }}</SelectItem>
                          <SelectItem value="PROCEDURE">{{ t('knowledge.termTypeProcedure') }}</SelectItem>
                        </SelectContent>
                      </Select>
                    </div>
                    <div class="space-y-2">
                      <Label>{{ t('knowledge.normalizedTermColumn') }}</Label>
                      <Input v-model="synonymDraft.canonicalTerm" :placeholder="t('knowledge.canonicalTermPlaceholder')" />
                    </div>
                  </div>

                  <div class="space-y-2">
                    <Label>{{ t('knowledge.variantTermsColumn') }}</Label>
                    <div
                      v-for="(member, index) in synonymDraft.members"
                      :key="member.id ?? `new-${index}`"
                      class="flex items-center gap-2"
                    >
                      <Input v-model="member.variantTerm" :placeholder="t('knowledge.variantTermPlaceholder')" class="flex-1" />
                      <div class="flex items-center gap-1 shrink-0">
                        <Switch v-model="member.isEnabled" :aria-label="t('knowledge.enabled')" />
                        <Button variant="ghost" size="sm" @click="removeSynonymMember(index)">
                          <X class="h-4 w-4" />
                        </Button>
                      </div>
                    </div>
                    <Button variant="outline" size="sm" @click="addSynonymMember">
                      <Plus class="h-4 w-4 mr-1" />
                      {{ t('knowledge.addVariantTerm') }}
                    </Button>
                  </div>

                  <DialogFooter>
                    <DialogClose as-child>
                      <Button type="button" variant="outline" :disabled="synonymSaving">{{ t('common.cancel') }}</Button>
                    </DialogClose>
                    <Button type="button" :disabled="synonymSaving" @click="saveSynonymDraft">
                      <Loader2 v-if="synonymSaving" class="h-4 w-4 mr-2 animate-spin" />
                      {{ t('knowledge.save') }}
                    </Button>
                  </DialogFooter>
                </div>
              </DialogContent>
            </Dialog>
          </TabsContent>

          <!-- 编码规则列表 -->
          <TabsContent value="rules" class="space-y-4">
            <div class="flex flex-col sm:flex-row gap-3">
              <Select v-model="codingRulesFilter" @update:model-value="onCodingRuleFilterChange">
                <SelectTrigger class="w-full sm:w-[200px]">
                  <SelectValue :placeholder="t('knowledge.filterByRuleCodeSystem')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__ALL__">{{ t('knowledge.allCodeSystems') }}</SelectItem>
                  <SelectItem v-for="cs in codeSystems" :key="cs.id" :value="cs.code">{{ cs.code }}</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <Card class="border-border/40">
              <CardContent class="pt-6">
                <div v-if="codingRulesLoading" class="flex items-center justify-center py-12">
                  <Loader2 class="h-5 w-5 animate-spin text-muted-foreground mr-2" />
                  <span class="text-sm text-muted-foreground">{{ t('knowledge.loading') }}</span>
                </div>
                <div v-else class="space-y-4">
                  <div class="overflow-x-auto">
                    <table class="w-full text-sm">
                      <thead>
                        <tr class="border-b border-border/40">
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.ruleCodeColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.codeSystemColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.codePatternColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.ruleTypeColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.severityColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.messageColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.statusColumn') }}</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="rule in codingRules" :key="rule.id" class="border-b border-border/20 hover:bg-muted/30 transition-colors">
                          <td class="py-3 px-4 font-mono text-xs">{{ rule.ruleCode }}</td>
                          <td class="py-3 px-4">
                            <Badge variant="outline">{{ rule.codeSystem }}</Badge>
                          </td>
                          <td class="py-3 px-4 font-mono text-xs">{{ rule.codePattern }}</td>
                          <td class="py-3 px-4 text-xs">{{ rule.ruleType }}</td>
                          <td class="py-3 px-4">
                            <Badge v-if="rule.severity === 'ERROR'" variant="destructive">{{ rule.severity }}</Badge>
                            <Badge v-else-if="rule.severity === 'WARNING'" class="bg-amber-500/15 text-amber-700">{{ rule.severity }}</Badge>
                            <Badge v-else variant="secondary">{{ rule.severity }}</Badge>
                          </td>
                          <td class="py-3 px-4 max-w-[250px] truncate text-xs" :title="rule.message">{{ rule.message }}</td>
                          <td class="py-3 px-4">
                            <Badge v-if="rule.isEnabled" variant="default" class="bg-emerald-500/15 text-emerald-700">{{ t('knowledge.enabled') }}</Badge>
                            <Badge v-else variant="secondary">{{ t('knowledge.disabled') }}</Badge>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  <div class="flex items-center justify-between">
                    <span class="text-sm text-muted-foreground">{{ t('knowledge.totalRecords', { total: codingRulesTotal }) }}</span>
                    <div class="flex items-center gap-2">
                      <Button variant="outline" size="sm" :disabled="codingRulesPage <= 1" @click="codingRulesPage--; loadCodingRules()">
                        <ChevronLeft class="h-4 w-4 mr-1" />
                        {{ t('knowledge.previousPage') }}
                      </Button>
                      <span class="text-sm text-muted-foreground">{{ t('knowledge.pagination', { page: codingRulesPage, totalPages: codingRulesTotalPages }) }}</span>
                      <Button variant="outline" size="sm" :disabled="codingRulesPage >= codingRulesTotalPages" @click="codingRulesPage++; loadCodingRules()">
                        {{ t('knowledge.nextPage') }}
                        <ChevronRight class="h-4 w-4 ml-1" />
                      </Button>
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>
          </TabsContent>

          <!-- ICD 索引库列表 -->
          <TabsContent value="icd-index" class="space-y-4">
            <div class="flex flex-col sm:flex-row gap-3">
              <div class="relative flex-1">
                <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                <Input
                  v-model="icdIndexKeyword"
                  :placeholder="t('knowledge.searchIcdIndexPlaceholder')"
                  class="pl-9"
                  @input="onIcdIndexSearchInput"
                />
              </div>
              <Select v-model="icdIndexStatusFilter" @update:model-value="onIcdIndexStatusFilterChange">
                <SelectTrigger class="w-full sm:w-[200px]">
                  <SelectValue :placeholder="t('knowledge.filterByLeadWordStatus')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__ALL__">{{ t('knowledge.allLeadWordStatus') }}</SelectItem>
                  <SelectItem value="0">{{ t('knowledge.leadWordValid') }}</SelectItem>
                  <SelectItem value="1">{{ t('knowledge.leadWordInvalid') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <!-- 搜索模式：关键词命中的平铺结果 -->
            <Card v-if="icdIndexIsSearch" class="border-border/40 -mb-[38px]">
              <CardContent class="pt-2">
                <div v-if="icdIndexLoading" class="flex items-center justify-center py-12">
                  <Loader2 class="h-5 w-5 animate-spin text-muted-foreground mr-2" />
                  <span class="text-sm text-muted-foreground">{{ t('knowledge.loading') }}</span>
                </div>
                <div v-else-if="icdIndex.length === 0" class="text-center py-12 text-muted-foreground text-sm">
                  {{ t('knowledge.noIcdIndex') }}
                </div>
                <div v-else class="space-y-4">
                  <div ref="icdIndexTableWrap" class="overflow-auto" :style="{ height: `${icdIndexTableHeight}px` }">
                    <table class="w-full table-fixed text-sm">
                      <thead>
                        <tr class="border-b border-border/40">
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[8%]">{{ t('knowledge.seqNoColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[26%]">{{ t('knowledge.indexNameColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[16%]">{{ t('knowledge.indexCodeColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[36%]">{{ t('knowledge.fullIndexNameColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[16%]">{{ t('knowledge.leadWordStatusColumn') }}</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="row in icdIndex" :key="row.id" class="border-b border-border/20 hover:bg-muted/30 transition-colors">
                          <td class="py-3 px-4 font-mono text-xs whitespace-nowrap">{{ row.seqNo }}</td>
                          <td class="py-3 px-4 truncate" :title="row.indexName">{{ row.indexName }}</td>
                          <td class="py-3 px-4 font-mono text-xs">{{ row.indexCode || '-' }}</td>
                          <td class="py-3 px-4 text-muted-foreground text-xs truncate" :title="row.fullIndexName">{{ row.fullIndexName }}</td>
                          <td class="py-3 px-4">
                            <Badge v-if="row.leadWordStatus === 0" variant="default" class="bg-emerald-500/15 text-emerald-700">{{ t('knowledge.leadWordValid') }}</Badge>
                            <Badge v-else variant="secondary">{{ t('knowledge.leadWordInvalid') }}</Badge>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  <div class="flex items-center justify-between" data-icd-footer>
                    <span class="text-sm text-muted-foreground">{{ t('knowledge.totalRecords', { total: icdIndexTotal }) }}</span>
                    <div class="flex items-center gap-2">
                      <Button variant="outline" size="sm" :disabled="icdIndexPage <= 1" @click="icdIndexPage--; loadIcdIndexSearch()">
                        <ChevronLeft class="h-4 w-4 mr-1" />
                        {{ t('knowledge.previousPage') }}
                      </Button>
                      <span class="text-sm text-muted-foreground">{{ t('knowledge.pagination', { page: icdIndexPage, totalPages: icdIndexTotalPages }) }}</span>
                      <Button variant="outline" size="sm" :disabled="icdIndexPage >= icdIndexTotalPages" @click="icdIndexPage++; loadIcdIndexSearch()">
                        {{ t('knowledge.nextPage') }}
                        <ChevronRight class="h-4 w-4 ml-1" />
                      </Button>
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>

            <!-- 树模式：按父节点层级展开 -->
            <Card v-else class="border-border/40 -mb-[38px]">
              <CardContent class="pt-2">
                <div v-if="icdIndexTreeLoading" class="flex items-center justify-center py-12">
                  <Loader2 class="h-5 w-5 animate-spin text-muted-foreground mr-2" />
                  <span class="text-sm text-muted-foreground">{{ t('knowledge.loading') }}</span>
                </div>
                <div v-else-if="icdIndexRoots.length === 0" class="text-center py-12 text-muted-foreground text-sm">
                  {{ t('knowledge.noIcdIndex') }}
                </div>
                <div v-else class="space-y-4">
                  <div ref="icdIndexTableWrap" class="overflow-auto" :style="{ height: `${icdIndexTableHeight}px` }">
                    <table class="w-full table-fixed text-sm">
                      <thead>
                        <tr class="border-b border-border/40">
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[26%]">{{ t('knowledge.indexNameColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[8%]">{{ t('knowledge.seqNoColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[16%]">{{ t('knowledge.indexCodeColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[36%]">{{ t('knowledge.fullIndexNameColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[16%]">{{ t('knowledge.leadWordStatusColumn') }}</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="row in icdIndexVisibleRows" :key="row.node.seqNo" class="border-b border-border/20 hover:bg-muted/30 transition-colors">
                          <td class="py-2 pr-4 pl-4">
                            <div class="flex items-center min-w-0" :style="{ paddingLeft: `${row.depth * 24}px` }">
                              <button
                                v-if="row.node.hasChildren"
                                class="p-0.5 rounded hover:bg-muted/50 text-muted-foreground shrink-0"
                                :title="icdIndexExpanded[row.node.seqNo] ? t('knowledge.collapseNode') : t('knowledge.expandNode')"
                                @click="toggleIcdIndexNode(row.node)"
                              >
                                <ChevronDown v-if="icdIndexExpanded[row.node.seqNo]" class="h-4 w-4" />
                                <ChevronRight v-else class="h-4 w-4" />
                              </button>
                              <span v-else class="w-5 shrink-0" />
                              <span v-if="icdIndexNodeLoading[row.node.seqNo]" class="mr-1.5 shrink-0">
                                <Loader2 class="h-3.5 w-3.5 animate-spin text-muted-foreground" />
                              </span>
                              <span class="truncate min-w-0 flex-1" :title="row.node.indexName">{{ row.node.indexName }}</span>
                            </div>
                          </td>
                          <td class="py-2 px-4 font-mono text-xs">{{ row.node.seqNo }}</td>
                          <td class="py-2 px-4 font-mono text-xs">{{ row.node.indexCode || '-' }}</td>
                          <td class="py-2 px-4 text-muted-foreground text-xs truncate" :title="row.node.fullIndexName">{{ row.node.fullIndexName }}</td>
                          <td class="py-2 px-4">
                            <Badge v-if="row.node.leadWordStatus === 0" variant="default" class="bg-emerald-500/15 text-emerald-700">{{ t('knowledge.leadWordValid') }}</Badge>
                            <Badge v-else variant="secondary">{{ t('knowledge.leadWordInvalid') }}</Badge>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  <div class="flex items-center justify-between" data-icd-footer>
                    <span class="text-sm text-muted-foreground">{{ t('knowledge.icdIndexRootProgress', { loaded: icdIndexRoots.length, total: icdIndexRootTotal }) }}</span>
                    <Button variant="outline" size="sm" :disabled="icdIndexRoots.length >= icdIndexRootTotal || icdIndexMoreLoading" @click="loadMoreIcdIndexRoots">
                      <Loader2 v-if="icdIndexMoreLoading" class="h-4 w-4 mr-1 animate-spin" />
                      {{ t('knowledge.loadMoreRoots') }}
                    </Button>
                  </div>
                </div>
              </CardContent>
            </Card>
          </TabsContent>
          <!-- CM3 索引库列表 -->
          <TabsContent value="cm3-index" class="space-y-4">
            <div class="flex flex-col sm:flex-row gap-3">
              <div class="relative flex-1">
                <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
                <Input
                  v-model="cm3IndexKeyword"
                  :placeholder="t('knowledge.searchCm3IndexPlaceholder')"
                  class="pl-9"
                  @input="onCm3IndexSearchInput"
                />
              </div>
              <Select v-model="cm3IndexStatusFilter" @update:model-value="onCm3IndexStatusFilterChange">
                <SelectTrigger class="w-full sm:w-[200px]">
                  <SelectValue :placeholder="t('knowledge.filterByLeadWordStatus')" />
                </SelectTrigger>
                <SelectContent>
                  <SelectItem value="__ALL__">{{ t('knowledge.allLeadWordStatus') }}</SelectItem>
                  <SelectItem value="0">{{ t('knowledge.leadWordValid') }}</SelectItem>
                  <SelectItem value="1">{{ t('knowledge.leadWordInvalid') }}</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <!-- 搜索模式：关键词命中的平铺结果 -->
            <Card v-if="cm3IndexIsSearch" class="border-border/40 -mb-[38px]">
              <CardContent class="pt-2">
                <div v-if="cm3IndexLoading" class="flex items-center justify-center py-12">
                  <Loader2 class="h-5 w-5 animate-spin text-muted-foreground mr-2" />
                  <span class="text-sm text-muted-foreground">{{ t('knowledge.loading') }}</span>
                </div>
                <div v-else-if="cm3Index.length === 0" class="text-center py-12 text-muted-foreground text-sm">
                  {{ t('knowledge.noCm3Index') }}
                </div>
                <div v-else class="space-y-4">
                  <div ref="cm3IndexTableWrap" class="overflow-auto" :style="{ height: `${cm3IndexTableHeight}px` }">
                    <table class="w-full table-fixed text-sm">
                      <thead>
                        <tr class="border-b border-border/40">
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[8%]">{{ t('knowledge.seqNoColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[26%]">{{ t('knowledge.indexNameColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[16%]">{{ t('knowledge.indexCodeColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[36%]">{{ t('knowledge.fullIndexNameColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[16%]">{{ t('knowledge.leadWordStatusColumn') }}</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="row in cm3Index" :key="row.id" class="border-b border-border/20 hover:bg-muted/30 transition-colors">
                          <td class="py-3 px-4 font-mono text-xs whitespace-nowrap">{{ row.seqNo }}</td>
                          <td class="py-3 px-4 truncate" :title="row.indexName">{{ row.indexName }}</td>
                          <td class="py-3 px-4 font-mono text-xs">{{ row.indexCode || '-' }}</td>
                          <td class="py-3 px-4 text-muted-foreground text-xs truncate" :title="row.fullIndexName">{{ row.fullIndexName }}</td>
                          <td class="py-3 px-4">
                            <Badge v-if="row.leadWordStatus === 0" variant="default" class="bg-emerald-500/15 text-emerald-700">{{ t('knowledge.leadWordValid') }}</Badge>
                            <Badge v-else variant="secondary">{{ t('knowledge.leadWordInvalid') }}</Badge>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  <div class="flex items-center justify-between" data-cm3-footer>
                    <span class="text-sm text-muted-foreground">{{ t('knowledge.totalRecords', { total: cm3IndexTotal }) }}</span>
                    <div class="flex items-center gap-2">
                      <Button variant="outline" size="sm" :disabled="cm3IndexPage <= 1" @click="cm3IndexPage--; loadCm3IndexSearch()">
                        <ChevronLeft class="h-4 w-4 mr-1" />
                        {{ t('knowledge.previousPage') }}
                      </Button>
                      <span class="text-sm text-muted-foreground">{{ t('knowledge.pagination', { page: cm3IndexPage, totalPages: cm3IndexTotalPages }) }}</span>
                      <Button variant="outline" size="sm" :disabled="cm3IndexPage >= cm3IndexTotalPages" @click="cm3IndexPage++; loadCm3IndexSearch()">
                        {{ t('knowledge.nextPage') }}
                        <ChevronRight class="h-4 w-4 ml-1" />
                      </Button>
                    </div>
                  </div>
                </div>
              </CardContent>
            </Card>

            <!-- 树模式：按父节点层级展开 -->
            <Card v-else class="border-border/40 -mb-[38px]">
              <CardContent class="pt-2">
                <div v-if="cm3IndexTreeLoading" class="flex items-center justify-center py-12">
                  <Loader2 class="h-5 w-5 animate-spin text-muted-foreground mr-2" />
                  <span class="text-sm text-muted-foreground">{{ t('knowledge.loading') }}</span>
                </div>
                <div v-else-if="cm3IndexRoots.length === 0" class="text-center py-12 text-muted-foreground text-sm">
                  {{ t('knowledge.noCm3Index') }}
                </div>
                <div v-else class="space-y-4">
                  <div ref="cm3IndexTableWrap" class="overflow-auto" :style="{ height: `${cm3IndexTableHeight}px` }">
                    <table class="w-full table-fixed text-sm">
                      <thead>
                        <tr class="border-b border-border/40">
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[26%]">{{ t('knowledge.indexNameColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[8%]">{{ t('knowledge.seqNoColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[16%]">{{ t('knowledge.indexCodeColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[36%]">{{ t('knowledge.fullIndexNameColumn') }}</th>
                          <th class="sticky top-0 z-10 bg-card text-left py-3 px-3 font-medium text-muted-foreground truncate w-[16%]">{{ t('knowledge.leadWordStatusColumn') }}</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="row in cm3IndexVisibleRows" :key="row.node.seqNo" class="border-b border-border/20 hover:bg-muted/30 transition-colors">
                          <td class="py-2 pr-4 pl-4">
                            <div class="flex items-center min-w-0" :style="{ paddingLeft: `${row.depth * 24}px` }">
                              <button
                                v-if="row.node.hasChildren"
                                class="p-0.5 rounded hover:bg-muted/50 text-muted-foreground shrink-0"
                                :title="cm3IndexExpanded[row.node.seqNo] ? t('knowledge.collapseNode') : t('knowledge.expandNode')"
                                @click="toggleCm3IndexNode(row.node)"
                              >
                                <ChevronDown v-if="cm3IndexExpanded[row.node.seqNo]" class="h-4 w-4" />
                                <ChevronRight v-else class="h-4 w-4" />
                              </button>
                              <span v-else class="w-5 shrink-0" />
                              <span v-if="cm3IndexNodeLoading[row.node.seqNo]" class="mr-1.5 shrink-0">
                                <Loader2 class="h-3.5 w-3.5 animate-spin text-muted-foreground" />
                              </span>
                              <span class="truncate min-w-0 flex-1" :title="row.node.indexName">{{ row.node.indexName }}</span>
                            </div>
                          </td>
                          <td class="py-2 px-4 font-mono text-xs">{{ row.node.seqNo }}</td>
                          <td class="py-2 px-4 font-mono text-xs">{{ row.node.indexCode || '-' }}</td>
                          <td class="py-2 px-4 text-muted-foreground text-xs truncate" :title="row.node.fullIndexName">{{ row.node.fullIndexName }}</td>
                          <td class="py-2 px-4">
                            <Badge v-if="row.node.leadWordStatus === 0" variant="default" class="bg-emerald-500/15 text-emerald-700">{{ t('knowledge.leadWordValid') }}</Badge>
                            <Badge v-else variant="secondary">{{ t('knowledge.leadWordInvalid') }}</Badge>
                          </td>
                        </tr>
                      </tbody>
                    </table>
                  </div>

                  <div class="flex items-center justify-between" data-cm3-footer>
                    <span class="text-sm text-muted-foreground">{{ t('knowledge.cm3IndexRootProgress', { loaded: cm3IndexRoots.length, total: cm3IndexRootTotal }) }}</span>
                    <Button variant="outline" size="sm" :disabled="cm3IndexRoots.length >= cm3IndexRootTotal || cm3IndexMoreLoading" @click="loadMoreCm3IndexRoots">
                      <Loader2 v-if="cm3IndexMoreLoading" class="h-4 w-4 mr-1 animate-spin" />
                      {{ t('knowledge.loadMoreRoots') }}
                    </Button>
                  </div>
                </div>
              </CardContent>
            </Card>
          </TabsContent>
        </Tabs>
      </TabsContent>

      <!-- ============ 编码体系导入 ============ -->
      <TabsContent value="code-system" class="space-y-6">
        <Card class="border border-border/40">
          <CardHeader>
            <CardTitle class="text-base">{{ t('knowledge.codeSystemImport') }}</CardTitle>
            <CardDescription>{{ t('knowledge.codeSystemImportDesc') }}</CardDescription>
          </CardHeader>
          <CardContent class="space-y-4">
            <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
              <div class="space-y-2">
                <Label>{{ t('knowledge.codeSystem') }}</Label>
                <Select v-model="codeSystemForm.codeSystem">
                  <SelectTrigger>
                    <SelectValue :placeholder="t('knowledge.codeSystemPlaceholder')" />
                  </SelectTrigger>
                  <SelectContent>
                    <SelectItem value="ICD-10">{{ t('knowledge.icd10') }}</SelectItem>
                    <SelectItem value="ICD-9-CM-3">{{ t('knowledge.icd9cm3') }}</SelectItem>
                  </SelectContent>
                </Select>
              </div>
              <div class="space-y-2">
                <Label>{{ t('knowledge.version') }}</Label>
                <Input v-model="codeSystemForm.version" :placeholder="t('knowledge.versionPlaceholder')" />
              </div>
            </div>

            <div class="flex items-center gap-3">
              <Button @click="handleImportCodeSystem" :disabled="codeSystemLoading">
                <Upload v-if="!codeSystemLoading" class="h-4 w-4 mr-1.5" />
                <Loader2 v-else class="h-4 w-4 mr-1.5 animate-spin" />
                {{ t('knowledge.importCodes') }} ({{ codeSystemForm.codeSystem }})
              </Button>
              <Button variant="outline" @click="codeSystemForm.codeSystem = codeSystemForm.codeSystem === 'ICD-10' ? 'ICD-9-CM-3' : 'ICD-10'">
                <Download class="h-4 w-4 mr-1.5" />
                {{ t('knowledge.loadSampleData') }}
              </Button>
            </div>

            <div v-if="codeSystemResult" class="p-4 bg-emerald-500/10 border border-emerald-500/20 rounded-lg">
              <div class="flex items-center gap-2 mb-2">
                <CheckCircle2 class="h-4 w-4 text-emerald-600" />
                <span class="text-sm font-medium text-emerald-700">{{ t('knowledge.importSuccess') }}</span>
              </div>
              <div class="grid grid-cols-2 gap-4 text-sm">
                <div>
                  <span class="text-muted-foreground">{{ t('knowledge.importedCount') }}: </span>
                  <span class="font-semibold text-emerald-700">{{ codeSystemResult.importedCount }}</span>
                </div>
                <div>
                  <span class="text-muted-foreground">{{ t('knowledge.updatedCount') }}: </span>
                  <span class="font-semibold text-emerald-700">{{ codeSystemResult.updatedCount }}</span>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>
      </TabsContent>

      <!-- ============ 规则导入 ============ -->
      <TabsContent value="rules" class="space-y-6">
        <Card class="border border-border/40">
          <CardHeader>
            <CardTitle class="text-base">{{ t('knowledge.rulesImport') }}</CardTitle>
            <CardDescription>{{ t('knowledge.rulesImportDesc') }}</CardDescription>
          </CardHeader>
          <CardContent class="space-y-4">
            <div class="space-y-2">
              <div class="flex items-center justify-between">
                <Label>{{ t('knowledge.synonyms') }} (JSON)</Label>
                <Button variant="ghost" size="sm" @click="loadSampleSynonyms">
                  <FileJson class="h-3 w-3 mr-1" />
                  {{ t('knowledge.loadSampleData') }}
                </Button>
              </div>
              <Textarea
                v-model="rulesForm.synonymsJson"
                :placeholder="'[\n  { &quot;term&quot;: &quot;感冒&quot;, &quot;normalizedTerm&quot;: &quot;急性鼻咽炎&quot;, &quot;entityType&quot;: &quot;DISEASE&quot;, &quot;codeSystemCode&quot;: &quot;ICD-10&quot;, &quot;code&quot;: &quot;J00&quot; }\n]'"
                rows="6"
                class="font-mono text-xs"
              />
            </div>

            <Separator />

            <div class="space-y-2">
              <div class="flex items-center justify-between">
                <Label>{{ t('knowledge.rules') }} (JSON)</Label>
                <Button variant="ghost" size="sm" @click="loadSampleRules">
                  <FileJson class="h-3 w-3 mr-1" />
                  {{ t('knowledge.loadSampleData') }}
                </Button>
              </div>
              <Textarea
                v-model="rulesForm.rulesJson"
                :placeholder="'[\n  { &quot;ruleCode&quot;: &quot;SZ-001&quot;, &quot;codeSystem&quot;: &quot;ICD-10&quot;, &quot;codePattern&quot;: &quot;J00-J06&quot;, &quot;ruleType&quot;: &quot;VALIDATION&quot;, &quot;severity&quot;: &quot;WARNING&quot;, &quot;message&quot;: &quot;上呼吸道感染编码需确认病程&quot;, &quot;isEnabled&quot;: true }\n]'"
                rows="6"
                class="font-mono text-xs"
              />
            </div>

            <Button @click="handleImportRules" :disabled="rulesLoading">
              <Upload v-if="!rulesLoading" class="h-4 w-4 mr-1.5" />
              <Loader2 v-else class="h-4 w-4 mr-1.5 animate-spin" />
              {{ t('knowledge.importRules') }}
            </Button>

            <div v-if="rulesResult" class="p-4 bg-emerald-500/10 border border-emerald-500/20 rounded-lg">
              <div class="flex items-center gap-2 mb-2">
                <CheckCircle2 class="h-4 w-4 text-emerald-600" />
                <span class="text-sm font-medium text-emerald-700">{{ t('knowledge.rulesImportSuccess') }}</span>
              </div>
              <div class="grid grid-cols-2 gap-4 text-sm">
                <div>
                  <span class="text-muted-foreground">{{ t('knowledge.synonymCount') }}: </span>
                  <span class="font-semibold text-emerald-700">{{ rulesResult.synonymCount }}</span>
                </div>
                <div>
                  <span class="text-muted-foreground">{{ t('knowledge.ruleCount') }}: </span>
                  <span class="font-semibold text-emerald-700">{{ rulesResult.ruleCount }}</span>
                </div>
              </div>
            </div>
          </CardContent>
        </Card>
      </TabsContent>
    </Tabs>
  </div>
</template>
