<script setup lang="ts">
import {
  BookOpen,
  CheckCircle2,
  ChevronLeft,
  ChevronRight,
  Database,
  Download,
  FileJson,
  Loader2,
  Search,
  Upload,
} from 'lucide-vue-next'
import { computed, onMounted, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, knowledgeApi } from '@/api'
import type {
  CodeSystemDto,
  CodingRuleDto,
  ImportCodeSystemResponse,
  ImportCodingRulesResponse,
  MedicalCodeDto,
  TermSynonymDto,
} from '@/api'
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardDescription,
  CardHeader,
  CardTitle,
  Input,
  Label,
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
  Separator,
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
const synonyms = ref<TermSynonymDto[]>([])
const synonymsTotal = ref(0)
const synonymsPage = ref(1)
const synonymsPageSize = 20
const synonymsLoading = ref(false)
const synonymsKeyword = ref('')

const synonymsTotalPages = computed(() =>
  Math.max(1, Math.ceil(synonymsTotal.value / synonymsPageSize)),
)

async function loadSynonyms() {
  synonymsLoading.value = true
  try {
    const result = await knowledgeApi.getTermSynonyms({
      page: synonymsPage.value,
      pageSize: synonymsPageSize,
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

/* ============== 浏览 Tab 切换加载 ============== */
const browseTab = ref('code-systems')

watch(browseTab, (tab) => {
  if (tab === 'code-systems' && codeSystems.value.length === 0) loadCodeSystems()
  if (tab === 'medical-codes' && medicalCodes.value.length === 0) loadMedicalCodes()
  if (tab === 'synonyms' && synonyms.value.length === 0) loadSynonyms()
  if (tab === 'rules' && codingRules.value.length === 0) loadCodingRules()
})

onMounted(() => {
  loadCodeSystems()
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
    <!-- 页面标题 -->
    <div class="flex items-center gap-4">
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
        <!-- 统计卡片 -->
        <div class="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-4 gap-4">
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
            <div class="relative max-w-md">
              <Search class="absolute left-3 top-1/2 -translate-y-1/2 h-4 w-4 text-muted-foreground" />
              <Input
                v-model="synonymsKeyword"
                :placeholder="t('knowledge.searchSynonymPlaceholder')"
                class="pl-9"
                @input="onSynonymSearchInput"
              />
            </div>

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
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.termColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.normalizedTermColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.entityTypeColumn') }}</th>
                          <th class="text-left py-3 px-4 font-medium text-muted-foreground">{{ t('knowledge.relatedCodeColumn') }}</th>
                        </tr>
                      </thead>
                      <tbody>
                        <tr v-for="syn in synonyms" :key="syn.id" class="border-b border-border/20 hover:bg-muted/30 transition-colors">
                          <td class="py-3 px-4">{{ syn.term }}</td>
                          <td class="py-3 px-4">{{ syn.normalizedTerm }}</td>
                          <td class="py-3 px-4">
                            <Badge variant="outline">{{ syn.entityType }}</Badge>
                          </td>
                          <td class="py-3 px-4 text-muted-foreground text-xs">
                            <span v-if="syn.codeSystemCode && syn.code">{{ syn.codeSystemCode }}: {{ syn.code }}</span>
                            <span v-else>-</span>
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
