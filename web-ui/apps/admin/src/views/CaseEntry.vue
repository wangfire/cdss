<script setup lang="ts">
import { CheckCircle2, FilePlus2, Loader2, ArrowRight, Sparkles } from 'lucide-vue-next'
import { computed, nextTick, onBeforeUnmount, onMounted, reactive, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { useRouter } from 'vue-router'
import { ApiError, caseEntryApi, workbenchApi } from '@/api'
import type { CaseEntryResponse, CodingRecommendationResponse } from '@/api'
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  Input,
  Label,
  Textarea,
  toast,
} from '@tabtab/ui'

const { t } = useI18n()
const router = useRouter()

const today = new Date().toISOString().slice(0, 10)

const form = reactive({
  patientName: '',
  medicalRecordNo: '',
  admissionCount: '' as string | number,
  admissionAt: today,
  dischargeAt: '',
  admissionDiagnoses: '',
  dischargeDiagnoses: '',
  procedures: '',
  additionalDocumentContent: '',
})

const submitting = ref(false)
const result = ref<CaseEntryResponse | null>(null)
const recs = ref<CodingRecommendationResponse[]>([])
const loadingRecs = ref(false)

function parseLines(text: string): string[] {
  return text
    .split('\n')
    .map((item) => item.trim())
    .filter((item) => item.length > 0)
}

const canSubmit = computed(
  () =>
    form.patientName.trim().length > 0
    && form.medicalRecordNo.trim().length > 0
    && form.admissionAt.length > 0
    && parseLines(form.dischargeDiagnoses).length > 0,
)

function toIsoDate(value: string): string | undefined {
  if (!value) return undefined
  // 本地零点上送，避免 UTC 偏移导致日期漂移一天。
  return new Date(`${value}T00:00:00`).toISOString()
}

async function submit() {
  if (!canSubmit.value || submitting.value) return
  submitting.value = true
  result.value = null
  recs.value = []
  try {
    const resp = await caseEntryApi.createCase({
      patientName: form.patientName.trim(),
      medicalRecordNo: form.medicalRecordNo.trim(),
      // type=number 输入框 v-model 可能给出数字，统一走 Number 归一。
      admissionCount: Number.isFinite(Number(form.admissionCount)) && Number(form.admissionCount) > 0
        ? Number(form.admissionCount)
        : null,
      admissionAt: toIsoDate(form.admissionAt) as string,
      dischargeAt: toIsoDate(form.dischargeAt) ?? null,
      admissionDiagnoses: parseLines(form.admissionDiagnoses),
      dischargeDiagnoses: parseLines(form.dischargeDiagnoses),
      procedures: parseLines(form.procedures),
      additionalDocumentContent: form.additionalDocumentContent.trim() || null,
    })
    result.value = resp
    toast.success(t('caseEntry.successTitle'))
    loadingRecs.value = true
    try {
      const detail = await workbenchApi.getRecommendations(resp.codingTaskId)
      recs.value = detail.recommendations
    } catch {
      toast.error(t('caseEntry.recFetchFailed'))
    } finally {
      loadingRecs.value = false
    }
  } catch (err: any) {
    // 非 ApiError（如模块异常）也要暴露真实原因，避免只剩笼统"录入失败"。
    toast.error(err instanceof ApiError ? err.message : (err?.message || t('caseEntry.submitFailed')))
  } finally {
    submitting.value = false
  }
}

interface RecGroup {
  key: string
  text: string
  sourceType: string
  isPrincipal: boolean
  recs: CodingRecommendationResponse[]
}

// 与工作台相同口径：ICD-9 为手术，DOCUMENT_AUTO/ADMISSION_PAGE 为入院诊断，其余为出院诊断。
function isProcedureRec(rec: CodingRecommendationResponse) {
  return rec.recommendationType === 'PROCEDURE' || rec.codeSystem.toUpperCase().includes('ICD-9')
}
function isAdmissionRec(rec: CodingRecommendationResponse) {
  return !isProcedureRec(rec)
    && (rec.diagnosisSourceType === 'DOCUMENT_AUTO' || rec.diagnosisSourceType === 'ADMISSION_PAGE')
}

const sections = computed(() => {
  const buckets: Record<'admission' | 'discharge' | 'procedure', CodingRecommendationResponse[]> = {
    admission: [],
    discharge: [],
    procedure: [],
  }
  for (const rec of recs.value) {
    if (isProcedureRec(rec)) buckets.procedure.push(rec)
    else if (isAdmissionRec(rec)) buckets.admission.push(rec)
    else buckets.discharge.push(rec)
  }
  const inputById = new Map((result.value?.diagnosisInputs ?? []).map((input) => [input.id, input]))
  const buildGroups = (list: CodingRecommendationResponse[]): RecGroup[] => {
    const byInput = new Map<string, RecGroup>()
    for (const rec of list) {
      const key = rec.diagnosisInputId ?? 'other'
      let group = byInput.get(key)
      if (!group) {
        const input = inputById.get(key)
        group = {
          key,
          text: input?.originalText ?? rec.doctorDiagnosisText ?? rec.title,
          sourceType: input?.sourceType ?? rec.diagnosisSourceType ?? '',
          isPrincipal: input ? input.isPrincipal : !!rec.isPrincipal,
          recs: [],
        }
        byInput.set(key, group)
      }
      group.recs.push(rec)
    }
    const arr = [...byInput.values()]
    for (const group of arr) group.recs.sort((a, b) => b.confidenceScore - a.confidenceScore)
    arr.sort((a, b) => Number(b.isPrincipal) - Number(a.isPrincipal))
    return arr
  }
  return [
    { key: 'admission', label: t('caseEntry.admissionDiagnoses'), groups: buildGroups(buckets.admission) },
    { key: 'discharge', label: t('caseEntry.dischargeDiagnoses'), groups: buildGroups(buckets.discharge) },
    { key: 'procedure', label: t('caseEntry.procedures'), groups: buildGroups(buckets.procedure) },
  ].filter((section) => section.groups.length > 0)
})

// 宽屏（≥xl 三栏）时把网格整体高度锁定为滚动容器可视区剩余高度，
// 滚动只发生在各列内部，页面级不再出现纵向滚动条。用"容器内绝对位置"测量，
// 不受测量时页面滚动位置影响。
const gridEl = ref<HTMLElement | null>(null)
function fitGrid() {
  const grid = gridEl.value
  if (!grid) return
  if (!window.matchMedia('(min-width: 1280px)').matches) {
    grid.style.height = ''
    return
  }
  let sc: HTMLElement | null = grid.parentElement
  while (sc) {
    const cs = getComputedStyle(sc)
    if (/(auto|scroll)/.test(cs.overflowY) && sc.scrollHeight > sc.clientHeight + 1) break
    sc = sc.parentElement
  }
  const rect = grid.getBoundingClientRect()
  const topAbs = sc ? rect.top - sc.getBoundingClientRect().top + sc.scrollTop : rect.top + window.scrollY
  const avail = (sc ? sc.clientHeight : window.innerHeight) - topAbs - 40
  grid.style.height = `${Math.max(420, Math.floor(avail))}px`
}
watch([result, submitting, loadingRecs], () => nextTick(fitGrid))
onMounted(() => {
  window.addEventListener('resize', fitGrid)
  nextTick(fitGrid)
})
onBeforeUnmount(() => window.removeEventListener('resize', fitGrid))

function resetForm() {
  Object.assign(form, {
    patientName: '',
    medicalRecordNo: '',
    admissionCount: '',
    admissionAt: today,
    dischargeAt: '',
    admissionDiagnoses: '',
    dischargeDiagnoses: '',
    procedures: '',
    additionalDocumentContent: '',
  })
  result.value = null
  recs.value = []
}

function goWorkbench() {
  if (result.value) {
    router.push(`/workbench?task=${result.value.codingTaskId}`)
  }
}
</script>

<template>
  <div class="w-full space-y-4">
    <div class="flex items-center gap-2">
      <FilePlus2 class="h-5 w-5 text-primary" />
      <h1 class="text-lg font-semibold">{{ t('caseEntry.title') }}</h1>
      <p class="text-xs text-muted-foreground ml-2">{{ t('caseEntry.subtitle') }}</p>
    </div>

    <div ref="gridEl" class="grid grid-cols-1 xl:grid-cols-[280px_minmax(340px,1fr)_minmax(400px,1.15fr)] gap-4 items-start xl:items-stretch">
      <!-- 左：基本信息 -->
      <Card class="border border-border/40 xl:h-full flex flex-col">
        <CardHeader class="pb-3">
          <CardTitle class="text-sm font-semibold">{{ t('caseEntry.basicInfo') }}</CardTitle>
        </CardHeader>
        <CardContent class="space-y-3">
          <div class="space-y-1.5">
            <Label>{{ t('caseEntry.patientName') }} <span class="text-destructive">*</span></Label>
            <Input v-model="form.patientName" :placeholder="t('caseEntry.patientNamePlaceholder')" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('caseEntry.medicalRecordNo') }} <span class="text-destructive">*</span></Label>
            <Input v-model="form.medicalRecordNo" :placeholder="t('caseEntry.medicalRecordNoPlaceholder')" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('caseEntry.admissionCount') }}</Label>
            <Input v-model="form.admissionCount" type="number" min="1" :placeholder="t('caseEntry.admissionCountPlaceholder')" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('caseEntry.admissionAt') }} <span class="text-destructive">*</span></Label>
            <Input v-model="form.admissionAt" type="date" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('caseEntry.dischargeAt') }}</Label>
            <Input v-model="form.dischargeAt" type="date" />
          </div>
        </CardContent>
      </Card>

      <!-- 中：诊断与手术 -->
      <Card class="border border-border/40 xl:h-full flex flex-col min-h-0">
        <CardHeader class="pb-3 shrink-0">
          <CardTitle class="text-sm font-semibold">{{ t('caseEntry.clinical') }}</CardTitle>
        </CardHeader>
        <CardContent class="space-y-3 flex-1 min-h-0 xl:overflow-y-auto">
          <div class="space-y-1.5">
            <Label>{{ t('caseEntry.admissionDiagnoses') }}</Label>
            <p class="text-[11px] text-muted-foreground">{{ t('caseEntry.onePerLine') }}</p>
            <Textarea v-model="form.admissionDiagnoses" rows="4" class="text-sm" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('caseEntry.dischargeDiagnoses') }} <span class="text-destructive">*</span></Label>
            <p class="text-[11px] text-muted-foreground">{{ t('caseEntry.principalHint') }}</p>
            <Textarea v-model="form.dischargeDiagnoses" rows="4" class="text-sm" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('caseEntry.procedures') }}</Label>
            <Textarea v-model="form.procedures" rows="3" class="text-sm" />
          </div>
          <div class="space-y-1.5">
            <Label>{{ t('caseEntry.additionalDocument') }}</Label>
            <Textarea
              v-model="form.additionalDocumentContent"
              rows="3"
              class="text-sm"
              :placeholder="t('caseEntry.additionalDocumentPlaceholder')"
            />
          </div>
          <div class="space-y-2 pt-1">
            <div class="flex items-center gap-3">
              <Button :disabled="!canSubmit || submitting" @click="submit">
                <Loader2 v-if="submitting" class="h-4 w-4 mr-1 animate-spin" />
                <CheckCircle2 v-else class="h-4 w-4 mr-1" />
                {{ submitting ? t('caseEntry.submitting') : t('caseEntry.submit') }}
              </Button>
              <Button v-if="result" variant="outline" @click="resetForm">
                {{ t('caseEntry.another') }}
              </Button>
            </div>
            <p v-if="submitting" class="text-xs text-muted-foreground">{{ t('caseEntry.submitTimeoutHint') }}</p>
          </div>
        </CardContent>
      </Card>

      <!-- 右：AI 推荐结果 -->
      <Card class="border border-border/40 xl:h-full flex flex-col min-h-0">
        <CardHeader class="pb-3 shrink-0">
          <div class="flex items-center justify-between gap-2">
            <CardTitle class="text-sm font-semibold flex items-center gap-1.5">
              <Sparkles class="h-4 w-4 text-primary" />
              {{ t('caseEntry.recPanelTitle') }}
            </CardTitle>
            <Badge v-if="result && !submitting" variant="outline" class="text-xs shrink-0">
              {{ t('caseEntry.recCount') }} {{ recs.length }}
            </Badge>
          </div>
          <div v-if="result && !submitting" class="flex items-center gap-2 text-xs text-muted-foreground pt-1">
            <CheckCircle2 class="h-3.5 w-3.5 text-emerald-500" />
            <span>{{ form.patientName || result.diagnosisInputs[0]?.originalText }} · 第 {{ result.admissionCount }} 次住院 · {{ result.pipelineVersion }}</span>
            <button class="ml-auto text-primary hover:underline flex items-center gap-1" @click="goWorkbench">
              {{ t('caseEntry.goWorkbench') }}
              <ArrowRight class="h-3 w-3" />
            </button>
          </div>
        </CardHeader>
        <CardContent class="flex-1 min-h-0 flex flex-col">
          <div v-if="submitting || loadingRecs" class="flex items-center gap-2 text-sm text-muted-foreground py-8 justify-center">
            <Loader2 class="h-4 w-4 animate-spin" />
            {{ submitting ? t('caseEntry.recPanelRunning') : t('caseEntry.recPanelLoading') }}
          </div>
          <div v-else-if="!result" class="text-sm text-muted-foreground py-8 text-center">
            {{ t('caseEntry.recPanelEmpty') }}
          </div>
          <div v-else class="flex-1 min-h-0 overflow-y-auto pr-1 space-y-5">
            <div v-if="sections.length === 0" class="text-sm text-muted-foreground py-6 text-center">
              {{ t('caseEntry.recPanelNone') }}
            </div>
            <div v-for="section in sections" :key="section.key" class="space-y-2">
              <div class="flex items-center gap-1.5 sticky top-0 z-10 bg-card py-1">
                <span class="text-xs font-semibold">{{ section.label }}</span>
                <Badge variant="outline" class="text-[10px] font-normal">{{ section.groups.length }}</Badge>
              </div>
              <div v-for="group in section.groups" :key="group.key" class="space-y-1.5">
                <div class="flex items-center gap-1.5 text-xs">
                  <Badge v-if="group.isPrincipal" class="text-[10px] font-normal">{{ t('caseEntry.principal') }}</Badge>
                  <span class="font-medium text-foreground">{{ group.text }}</span>
                </div>
                <div
                  v-for="rec in group.recs"
                  :key="rec.id"
                  class="flex items-center gap-2 rounded-md border border-border/40 px-2.5 py-1.5 text-sm"
                >
                  <span class="text-xs text-muted-foreground w-4 shrink-0">{{ rec.rank }}</span>
                  <code class="font-mono text-xs bg-muted rounded px-1.5 py-0.5 shrink-0">{{ rec.code }}</code>
                  <span class="truncate flex-1">{{ rec.title }}</span>
                  <span class="text-xs text-muted-foreground shrink-0">{{ (rec.confidenceScore * 100).toFixed(1) }}%</span>
                </div>
              </div>
            </div>
          </div>
        </CardContent>
      </Card>
    </div>
  </div>
</template>
