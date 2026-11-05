<script setup lang="ts">
import { CheckCircle2, ClipboardList, Loader2, Plus, Sparkles, Star, Trash2 } from 'lucide-vue-next'
import { computed, reactive, ref } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, codingEntryApi, workbenchApi } from '@/api'
import type {
  FinalResultItem,
  MedicalCodeDto,
  NameLookupResponse,
} from '@/api'
import DictionaryPickerDialog from '@/components/DictionaryPickerDialog.vue'
import {
  Badge,
  Button,
  Card,
  CardContent,
  CardHeader,
  CardTitle,
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Input,
  Label,
  Tabs,
  TabsList,
  TabsTrigger,
  Textarea,
  toast,
} from '@tabtab/ui'

type EntryKind = 'diagnosis' | 'procedure'

/** 录入条目：一条诊断或手术。确认编码后 code/title 落到条目上，保存时随病例提交最终编码。 */
interface EntryItem {
  key: string
  kind: EntryKind
  text: string
  code: string | null
  title: string | null
  source: 'manual' | 'dictionary'
  isPrincipal: boolean
  lookup: NameLookupResponse | null
  loading: boolean
}

const { t } = useI18n()

const kind = ref<EntryKind>('diagnosis')
const nameInput = ref('')
const entries = ref<EntryItem[]>([])
const activeKey = ref<string | null>(null)
const parsing = ref(false)
const pickerOpen = ref(false)

const saveOpen = ref(false)
const saving = ref(false)
const saveForm = ref({
  patientName: '',
  medicalRecordNo: '',
  admissionCount: '',
  admissionAt: new Date().toISOString().slice(0, 10),
  dischargeAt: '',
  additionalDocumentContent: '',
})

const activeEntry = computed(() => entries.value.find((item) => item.key === activeKey.value) ?? null)

const canSave = computed(() =>
  saveForm.value.patientName.trim().length > 0
  && saveForm.value.medicalRecordNo.trim().length > 0
  && entries.value.some((item) => item.kind === 'diagnosis'))

const confirmedItems = computed(() => entries.value.filter((item) => item.code))

function entryBadge(item: EntryItem) {
  return item.kind === 'procedure' ? t('codingEntry.entryProcedure') : t('codingEntry.entryDiagnosis')
}

function channelTextClass(on: boolean) {
  return on ? 'text-muted-foreground' : 'text-amber-600 font-medium'
}

function addEntry() {
  const text = nameInput.value.trim()
  if (!text) {
    toast.error(t('codingEntry.nameRequired'))
    return
  }

  const existing = entries.value.find(
    (item) => item.kind === kind.value && item.text === text)
  if (existing) {
    activeKey.value = existing.key
    nameInput.value = ''
    return
  }

  // reactive 包装：条目必须以代理身份进入列表，否则后续对 loading/lookup 的
  // 原始引用赋值不会触发 Vue 响应式，界面会永远停在加载态（实测踩坑）。
  const item = reactive<EntryItem>({
    key: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    kind: kind.value,
    text,
    code: null,
    title: null,
    source: 'manual',
    isPrincipal: false,
    lookup: null,
    loading: false,
  })
  entries.value.push(item)
  activeKey.value = item.key
  nameInput.value = ''
  void lookup(item)
}

function pickFromDictionary(row: MedicalCodeDto) {
  // 同 addEntry：reactive 包装保证字典选择后的解析进度与结果实时上屏。
  const item = reactive<EntryItem>({
    key: `${Date.now()}-${Math.random().toString(36).slice(2, 8)}`,
    kind: kind.value,
    text: row.title,
    code: row.code,
    title: row.title,
    source: 'dictionary',
    isPrincipal: false,
    lookup: null,
    loading: false,
  })
  entries.value.push(item)
  activeKey.value = item.key
  void lookup(item)
}

async function lookup(item: EntryItem) {
  item.loading = true
  try {
    item.lookup = await codingEntryApi.lookupName({ kind: item.kind, text: item.text })
    if (item.lookup.degradedFlags.length > 0) {
      toast.warning(t('codingEntry.degraded', { flags: item.lookup.degradedFlags.join(', ') }))
    }
  } catch (error) {
    toast.error(error instanceof ApiError ? error.message : t('codingEntry.lookupFailed'))
  } finally {
    item.loading = false
  }
}

function reparse(item: EntryItem) {
  item.code = null
  item.title = null
  item.source = 'manual'
  void lookup(item)
}

function choose(item: EntryItem, index: number) {
  const recommendation = item.lookup?.recommendations[index]
  if (!recommendation) return
  item.code = recommendation.code
  item.title = recommendation.title
  toast.success(t('codingEntry.chosenToast', { code: recommendation.code }))
}

function removeEntry(item: EntryItem) {
  entries.value = entries.value.filter((entry) => entry.key !== item.key)
  if (activeKey.value === item.key) {
    activeKey.value = entries.value[0]?.key ?? null
  }
}

function togglePrincipal(item: EntryItem) {
  if (item.kind !== 'diagnosis') return
  const next = !item.isPrincipal
  // 主诊断唯一：先清掉其它条目的主标记。
  entries.value.forEach((entry) => {
    if (entry.kind === 'diagnosis') entry.isPrincipal = false
  })
  item.isPrincipal = next
}

function toIsoDate(value: string): string | null {
  if (!value) return null
  return new Date(`${value}T00:00:00`).toISOString()
}

async function waitForTaskCompletion(taskId: string): Promise<'done' | 'failed' | 'timeout'> {
  const deadline = Date.now() + 120_000
  while (Date.now() < deadline) {
    await new Promise((resolve) => setTimeout(resolve, 2000))
    const task = await workbenchApi.getTask(taskId)
    if (task.status === 'FAILED') return 'failed'
    if (['SUCCESS', 'PENDING_REVIEW', 'HUMAN_REQUIRED'].includes(task.status)) return 'done'
  }
  return 'timeout'
}

async function saveAsCase() {
  if (!canSave.value || saving.value) return
  saving.value = true
  try {
    const diagnoses = entries.value.filter((item) => item.kind === 'diagnosis')
    const procedures = entries.value.filter((item) => item.kind === 'procedure')
    const orderedDiagnoses = [
      ...diagnoses.filter((item) => item.isPrincipal),
      ...diagnoses.filter((item) => !item.isPrincipal),
    ]
    const resp = await codingEntryApi.createCase({
      patientName: saveForm.value.patientName.trim(),
      medicalRecordNo: saveForm.value.medicalRecordNo.trim(),
      admissionCount: Number(saveForm.value.admissionCount) > 0
        ? Number(saveForm.value.admissionCount)
        : null,
      admissionAt: toIsoDate(saveForm.value.admissionAt) as string,
      dischargeAt: toIsoDate(saveForm.value.dischargeAt),
      admissionDiagnoses: [],
      dischargeDiagnoses: orderedDiagnoses.map((item) => item.text),
      procedures: procedures.map((item) => item.text),
      additionalDocumentContent: saveForm.value.additionalDocumentContent.trim() || null,
    })

    // 确认过编码的条目：等任务跑完后提交为最终编码（已确认结果不允许 AI 覆盖）。
    let finalCount = 0
    if (confirmedItems.value.length > 0) {
      const outcome = await waitForTaskCompletion(resp.codingTaskId)
      if (outcome === 'done') {
        const items: FinalResultItem[] = confirmedItems.value.map((item) => ({
          resultType: item.kind === 'procedure' ? 'PROCEDURE' : 'DIAGNOSIS',
          codeSystem: item.kind === 'procedure' ? 'ICD-9-CM-3' : 'ICD-10',
          code: item.code as string,
          title: item.title ?? item.text,
        }))
        const finalResp = await codingEntryApi.submitFinalResults({
          codingTaskId: resp.codingTaskId,
          items,
        })
        finalCount = finalResp.finalCodingCount
      }
    }

    toast.success(t('codingEntry.saveSuccess', { taskId: resp.codingTaskId.slice(0, 8) }))
    if (finalCount > 0) {
      toast.success(t('codingEntry.finalSubmitted', { count: finalCount }))
    }
    saveOpen.value = false
    saveForm.value = {
      patientName: '',
      medicalRecordNo: '',
      admissionCount: '',
      admissionAt: new Date().toISOString().slice(0, 10),
      dischargeAt: '',
      additionalDocumentContent: '',
    }
  } catch (error) {
    toast.error(error instanceof ApiError ? error.message : t('codingEntry.saveFailed'))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <div class="space-y-4">
    <!-- 类型切换 + 名称录入 -->
    <Card>
      <CardHeader class="pb-3">
        <CardTitle class="flex items-center gap-2 text-base">
          <ClipboardList class="h-4 w-4" />
          {{ t('codingEntry.title') }}
        </CardTitle>
      </CardHeader>
      <CardContent class="space-y-4">
        <Tabs v-model="kind">
          <TabsList>
            <TabsTrigger value="diagnosis">{{ t('codingEntry.tabDiagnosis') }}</TabsTrigger>
            <TabsTrigger value="procedure">{{ t('codingEntry.tabProcedure') }}</TabsTrigger>
          </TabsList>
        </Tabs>

        <div class="flex gap-2">
          <Input
            v-model="nameInput"
            class="flex-1"
            :placeholder="t('codingEntry.namePlaceholder')"
            @keyup.enter="addEntry"
          />
          <Button type="button" @click="addEntry">
            <Sparkles class="h-4 w-4 mr-1" />
            {{ t('codingEntry.parse') }}
          </Button>
          <Button type="button" variant="outline" @click="pickerOpen = true">
            {{ t('codingEntry.dictionaryBtn') }}
          </Button>
        </div>
        <p class="text-xs text-muted-foreground">{{ t('codingEntry.ruleHint') }}</p>
      </CardContent>
    </Card>

    <div class="grid gap-4 lg:grid-cols-5">
      <!-- 左：录入列表 -->
      <Card class="lg:col-span-2">
        <CardHeader class="pb-3">
          <CardTitle class="flex items-center justify-between text-base">
            <span>{{ t('codingEntry.entriesTitle') }}</span>
            <Badge variant="secondary">{{ entries.length }}</Badge>
          </CardTitle>
        </CardHeader>
        <CardContent class="space-y-2">
          <p v-if="entries.length === 0" class="py-8 text-center text-sm text-muted-foreground">
            {{ t('codingEntry.entriesEmpty') }}
          </p>
          <div
            v-for="item in entries"
            :key="item.key"
            class="rounded-md border p-3 cursor-pointer transition-colors"
            :class="activeKey === item.key ? 'border-primary bg-muted/40' : 'hover:bg-muted/30'"
            @click="activeKey = item.key"
          >
            <div class="flex items-start justify-between gap-2">
              <div class="min-w-0">
                <div class="flex items-center gap-1.5">
                  <Badge :variant="item.kind === 'procedure' ? 'outline' : 'default'" class="shrink-0">
                    {{ entryBadge(item) }}
                  </Badge>
                  <Badge v-if="item.isPrincipal" variant="secondary" class="shrink-0">
                    {{ t('codingEntry.principal') }}
                  </Badge>
                </div>
                <p class="mt-1 truncate text-sm font-medium">{{ item.text }}</p>
                <p v-if="item.code" class="mt-0.5 font-mono text-xs text-primary">
                  {{ item.code }} {{ item.title }}
                </p>
                <p v-else class="mt-0.5 text-xs text-muted-foreground">
                  {{ t('codingEntry.unconfirmed') }}
                </p>
              </div>
              <div class="flex shrink-0 gap-1">
                <Button
                  v-if="item.kind === 'diagnosis'"
                  type="button"
                  size="icon"
                  variant="ghost"
                  :title="t('codingEntry.markPrincipal')"
                  @click.stop="togglePrincipal(item)"
                >
                  <Star
                    class="h-4 w-4"
                    :class="item.isPrincipal ? 'fill-yellow-400 text-yellow-500' : 'text-muted-foreground'"
                  />
                </Button>
                <Button
                  type="button"
                  size="icon"
                  variant="ghost"
                  :title="t('codingEntry.removeEntry')"
                  @click.stop="removeEntry(item)"
                >
                  <Trash2 class="h-4 w-4 text-muted-foreground" />
                </Button>
              </div>
            </div>
          </div>
          <Button
            type="button"
            variant="outline"
            class="w-full"
            :disabled="!confirmedItems.length"
            @click="saveOpen = true"
          >
            <CheckCircle2 class="h-4 w-4 mr-1" />
            {{ t('codingEntry.saveAsCase') }}
          </Button>
        </CardContent>
      </Card>

      <!-- 右：解析面板 -->
      <Card class="lg:col-span-3">
        <CardHeader class="pb-3">
          <CardTitle class="flex items-center gap-2 text-base">
            {{ t('codingEntry.analysisTitle') }}
            <Loader2 v-if="activeEntry?.loading" class="h-4 w-4 animate-spin" />
          </CardTitle>
        </CardHeader>
        <CardContent
          v-if="!activeEntry || !activeEntry.lookup"
          class="py-12 text-center text-sm text-muted-foreground"
        >
          {{ t('codingEntry.analysisEmpty') }}
        </CardContent>
        <CardContent v-else class="space-y-5">
          <!-- 标题通道开关回显（与流水线运行时配置同源，工作台设置抽屉可调） -->
          <div class="flex flex-wrap items-center gap-x-3 gap-y-1 text-xs">
            <span class="text-muted-foreground">{{ t('codingEntry.titleChannelsLabel') }}</span>
            <span :class="channelTextClass(activeEntry.lookup.titleChannels.icdTitleDirect)">
              {{ t('codingEntry.channelIcdDirect') }}
              {{ activeEntry.lookup.titleChannels.icdTitleDirect ? t('codingEntry.channelOn') : t('codingEntry.channelOff') }}
            </span>
            <span :class="channelTextClass(activeEntry.lookup.titleChannels.cm3TitleDirect)">
              {{ t('codingEntry.channelCm3Direct') }}
              {{ activeEntry.lookup.titleChannels.cm3TitleDirect ? t('codingEntry.channelOn') : t('codingEntry.channelOff') }}
            </span>
            <span :class="channelTextClass(activeEntry.lookup.titleChannels.icdTitleReverse)">
              {{ t('codingEntry.channelReverse') }}
              {{ activeEntry.lookup.titleChannels.icdTitleReverse ? t('codingEntry.channelOn') : t('codingEntry.channelOff') }}
            </span>
            <span class="text-muted-foreground/70">（{{ t('codingEntry.titleChannelsHint') }}）</span>
          </div>

          <!-- 主导词 chips -->
          <div class="space-y-2">
            <p class="text-sm font-medium">{{ t('codingEntry.leadTermsTitle') }}</p>
            <div v-if="activeEntry.lookup.leadTerms.length === 0" class="text-xs text-muted-foreground">
              {{ t('codingEntry.leadTermsEmpty') }}
            </div>
            <div v-else class="flex flex-wrap gap-2">
              <Badge
                v-for="lead in activeEntry.lookup.leadTerms"
                :key="lead.seqNo"
                variant="outline"
                class="px-2.5 py-1"
                :title="[
                  t('codingEntry.fullTermLabel') + ': ' + lead.term,
                  `${t('codingEntry.hitAlias')}: ${lead.hitAlias} · ${t('codingEntry.coveredChars')}: ${lead.coveredChars}`,
                  lead.redirectKey ? t('codingEntry.redirectHint', { key: lead.redirectKey, target: lead.term }) : '',
                ].filter(Boolean).join(' · ')"
              >
                <template v-if="lead.redirectKey">{{ lead.redirectKey }} → {{ lead.displayTerm || lead.term }}</template>
                <template v-else>{{ lead.displayTerm || lead.term }}</template>
                <span class="ml-1 text-[10px] text-muted-foreground">{{ lead.coveredChars }}</span>
              </Badge>
            </div>
          </div>

          <!-- 修饰词链路 -->
          <div class="space-y-2">
            <p class="text-sm font-medium">{{ t('codingEntry.modifierPathsTitle') }}</p>
            <div
              v-for="lead in activeEntry.lookup.leadTerms"
              :key="`paths-${lead.seqNo}`"
              class="space-y-1.5"
            >
              <p class="text-xs font-medium text-muted-foreground">{{ lead.term }}</p>
              <p v-if="lead.modifierPaths.length === 0" class="text-xs text-muted-foreground/70">
                {{ t('codingEntry.noDrillPath') }}
              </p>
              <div
                v-for="(path, pathIndex) in lead.modifierPaths"
                :key="`${lead.seqNo}-${pathIndex}`"
                class="rounded-md border p-2.5 text-xs"
              >
                <div class="flex flex-wrap items-center gap-1">
                  <template v-for="(step, stepIndex) in path.path" :key="stepIndex">
                    <span v-if="stepIndex > 0" class="text-muted-foreground">→</span>
                    <span :class="stepIndex === 0 ? 'font-medium' : ''">{{ step }}</span>
                  </template>
                  <Badge v-if="path.seeAlsoHint" variant="secondary" class="ml-1">
                    {{ t('codingEntry.seeAlsoHint') }}
                  </Badge>
                  <Badge v-if="path.indexCode" variant="outline" class="ml-1 font-mono">
                    {{ path.indexCode }}
                  </Badge>
                </div>
                <div v-if="path.codes.length > 0" class="mt-1.5 space-y-1">
                  <div
                    v-for="code in path.codes"
                    :key="code.code"
                    class="flex items-center justify-between gap-2"
                  >
                    <span>
                      <span class="font-mono text-primary">{{ code.code }}</span>
                      <span class="ml-1.5">{{ code.title }}</span>
                      <Badge v-if="code.isIndexFallback" variant="secondary" class="ml-1">
                        {{ t('codingEntry.sourceFallback') }}
                      </Badge>
                    </span>
                  </div>
                </div>
              </div>
            </div>
          </div>

          <!-- 推荐编码 -->
          <div class="space-y-2">
            <p class="text-sm font-medium">{{ t('codingEntry.recommendationsTitle') }}</p>
            <p v-if="activeEntry.lookup.recommendations.length === 0" class="text-xs text-muted-foreground">
              {{ t('codingEntry.recommendationsEmpty') }}
            </p>
            <div class="space-y-2">
              <div
                v-for="(recommendation, index) in activeEntry.lookup.recommendations"
                :key="recommendation.code"
                class="flex items-start justify-between gap-3 rounded-md border p-3"
              >
                <div class="min-w-0">
                  <div class="flex flex-wrap items-center gap-1.5">
                    <span class="font-mono text-sm font-semibold text-primary">{{ recommendation.code }}</span>
                    <Badge v-if="recommendation.dictionaryDirect">{{ t('codingEntry.sourceDictionary') }}</Badge>
                    <Badge v-else-if="recommendation.isIndexFallback" variant="secondary">
                      {{ t('codingEntry.sourceFallback') }}
                    </Badge>
                    <Badge v-else variant="outline">{{ t('codingEntry.sourceIndex') }}</Badge>
                  </div>
                  <p class="mt-1 truncate text-sm">{{ recommendation.title }}</p>
                  <p v-if="recommendation.path.length > 0" class="mt-0.5 text-xs text-muted-foreground">
                    {{ t('codingEntry.leadWordLabel') }}: {{ recommendation.leadTerm }} ·
                    {{ recommendation.path.join(' → ') }}
                  </p>
                </div>
                <Button
                  type="button"
                  size="sm"
                  :variant="activeEntry.code === recommendation.code ? 'secondary' : 'outline'"
                  :disabled="activeEntry.code === recommendation.code"
                  @click="choose(activeEntry, index)"
                >
                  <CheckCircle2 v-if="activeEntry.code === recommendation.code" class="h-3.5 w-3.5 mr-1" />
                  {{ activeEntry.code === recommendation.code ? t('codingEntry.chosen') : t('codingEntry.choose') }}
                </Button>
              </div>
            </div>
            <Button
              v-if="activeEntry.code"
              type="button"
              size="sm"
              variant="ghost"
              class="w-full"
              @click="reparse(activeEntry)"
            >
              {{ t('codingEntry.resetChoice') }}
            </Button>
          </div>
        </CardContent>
      </Card>
    </div>

    <DictionaryPickerDialog
      v-model:open="pickerOpen"
      :kind="kind"
      @picked="pickFromDictionary"
    />

    <!-- 保存为病例 -->
    <Dialog :open="saveOpen" @update:open="saveOpen = $event">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{{ t('codingEntry.saveDialogTitle') }}</DialogTitle>
          <DialogDescription>{{ t('codingEntry.saveDialogHint') }}</DialogDescription>
        </DialogHeader>
        <div class="grid grid-cols-2 gap-3">
          <div class="space-y-2">
            <Label>{{ t('codingEntry.patientName') }}</Label>
            <Input v-model="saveForm.patientName" />
          </div>
          <div class="space-y-2">
            <Label>{{ t('codingEntry.medicalRecordNo') }}</Label>
            <Input v-model="saveForm.medicalRecordNo" />
          </div>
          <div class="space-y-2">
            <Label>{{ t('codingEntry.admissionCount') }}</Label>
            <Input v-model="saveForm.admissionCount" type="number" min="1" />
          </div>
          <div class="space-y-2">
            <Label>{{ t('codingEntry.admissionAt') }}</Label>
            <Input v-model="saveForm.admissionAt" type="date" />
          </div>
          <div class="space-y-2">
            <Label>{{ t('codingEntry.dischargeAt') }}</Label>
            <Input v-model="saveForm.dischargeAt" type="date" />
          </div>
        </div>
        <div class="space-y-2">
          <Label>{{ t('codingEntry.additionalDocumentContent') }}</Label>
          <Textarea
            v-model="saveForm.additionalDocumentContent"
            :placeholder="t('codingEntry.additionalDocumentPlaceholder')"
            rows="3"
          />
        </div>
        <div class="rounded-md border p-3 text-xs">
          <p class="font-medium">{{ t('codingEntry.saveItemsTitle') }}</p>
          <p v-for="item in entries" :key="`save-${item.key}`" class="mt-1 text-muted-foreground">
            {{ entryBadge(item) }}：{{ item.text }}
            <span v-if="item.code" class="font-mono text-primary">（{{ item.code }}）</span>
          </p>
        </div>
        <DialogFooter>
          <Button type="button" variant="outline" :disabled="saving" @click="saveOpen = false">
            {{ t('common.cancel') }}
          </Button>
          <Button type="button" :disabled="!canSave || saving" @click="saveAsCase">
            <Loader2 v-if="saving" class="h-4 w-4 mr-1 animate-spin" />
            <Plus v-else class="h-4 w-4 mr-1" />
            {{ t('codingEntry.save') }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
