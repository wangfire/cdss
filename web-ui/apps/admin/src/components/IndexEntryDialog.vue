<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { Loader2 } from 'lucide-vue-next'
import { ApiError, knowledgeApi } from '@/api'
import type {
  Cm3IndexDto,
  IcdIndexDto,
  IndexEntryInputDto,
  TumorIndexDto,
  TumorIndexEntryInputDto,
} from '@/api'
import {
  Button,
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
  toast,
} from '@tabtab/ui'

type IndexKind = 'icd' | 'cm3' | 'tumor'
type IndexEntryRow = IcdIndexDto | Cm3IndexDto | TumorIndexDto

const props = defineProps<{
  open: boolean
  kind: IndexKind
  /** 编辑时的原行；新增为空。 */
  entry?: IndexEntryRow | null
  /** 新增子级时的父节点（预填父序号与级别）。 */
  parent?: { seqNo: number; indexLevel: number; indexName: string } | null
}>()

const emit = defineEmits<{
  (event: 'update:open', value: boolean): void
  (event: 'saved', parentSeqNo: number): void
}>()

const { t } = useI18n()

const form = ref({
  seqNo: 0,
  rawName: '',
  rawNameEn: '',
  indexName: '',
  indexNameEn: '',
  indexCode: '',
  indexLevel: 0,
  parentSeqNo: 0,
  fullIndexName: '',
  leadWordStatus: 0,
  codeMalignantPrimary: '',
  codeMalignantSecondary: '',
  codeInSitu: '',
  codeBenign: '',
  codeUncertain: '',
  jumpKind: 0,
  jumpTarget: '',
})
const saving = ref(false)

const isEdit = computed(() => !!props.entry)
const title = computed(() => {
  if (isEdit.value) return t('knowledge.editIndexEntry')
  return props.parent ? t('knowledge.createChildIndexEntry') : t('knowledge.createIndexEntry')
})

function resetForm() {
  const entry = props.entry
  const parent = props.parent
  const icdLike = entry as (IcdIndexDto | Cm3IndexDto | undefined)
  const tumor = entry as (TumorIndexDto | undefined)
  form.value = {
    seqNo: entry?.seqNo ?? 0,
    rawName: icdLike?.rawName ?? '',
    rawNameEn: icdLike?.rawNameEn ?? '',
    indexName: entry?.indexName ?? '',
    indexNameEn: tumor?.indexNameEn ?? '',
    indexCode: icdLike?.indexCode ?? '',
    indexLevel: entry?.indexLevel ?? (parent ? parent.indexLevel + 1 : 0),
    parentSeqNo: entry?.parentSeqNo ?? (parent ? parent.seqNo : 0),
    fullIndexName: entry?.fullIndexName ?? '',
    leadWordStatus: icdLike?.leadWordStatus ?? 0,
    codeMalignantPrimary: tumor?.codeMalignantPrimary ?? '',
    codeMalignantSecondary: tumor?.codeMalignantSecondary ?? '',
    codeInSitu: tumor?.codeInSitu ?? '',
    codeBenign: tumor?.codeBenign ?? '',
    codeUncertain: tumor?.codeUncertain ?? '',
    jumpKind: tumor?.jumpKind ?? 0,
    jumpTarget: tumor?.jumpTarget ?? '',
  }
}

watch(
  () => props.open,
  (open) => {
    if (open) resetForm()
  },
)

async function saveEntry() {
  const f = form.value
  if (!isEdit.value && (!Number.isInteger(f.seqNo) || f.seqNo <= 0)) {
    toast.error(t('knowledge.seqNoRequired'))
    return
  }
  if (!f.indexName.trim()) {
    toast.error(t('knowledge.indexNameRequired'))
    return
  }
  if (!Number.isInteger(f.indexLevel) || f.indexLevel < 0 || f.indexLevel > 9) {
    toast.error(t('knowledge.indexLevelRange'))
    return
  }
  if (f.parentSeqNo < 0 || !Number.isInteger(f.parentSeqNo)) {
    toast.error(t('knowledge.parentSeqNoInvalid'))
    return
  }
  if (props.kind === 'tumor' && f.jumpKind !== 0 && !f.jumpTarget.trim()) {
    toast.error(t('knowledge.jumpTargetRequired'))
    return
  }

  saving.value = true
  try {
    if (props.kind === 'tumor') {
      const payload: TumorIndexEntryInputDto = {
        indexName: f.indexName.trim(),
        indexNameEn: f.indexNameEn.trim(),
        indexLevel: f.indexLevel,
        parentSeqNo: f.parentSeqNo,
        fullIndexName: f.fullIndexName.trim(),
        codeMalignantPrimary: f.codeMalignantPrimary.trim(),
        codeMalignantSecondary: f.codeMalignantSecondary.trim(),
        codeInSitu: f.codeInSitu.trim(),
        codeBenign: f.codeBenign.trim(),
        codeUncertain: f.codeUncertain.trim(),
        jumpKind: f.jumpKind,
        jumpTarget: f.jumpTarget.trim(),
      }
      if (props.entry) await knowledgeApi.updateTumorIndexEntry(props.entry.id, payload)
      else await knowledgeApi.createTumorIndexEntry({ ...payload, seqNo: f.seqNo })
    } else {
      const payload: IndexEntryInputDto = {
        rawName: f.rawName.trim(),
        rawNameEn: f.rawNameEn.trim(),
        indexName: f.indexName.trim(),
        indexCode: f.indexCode.trim(),
        indexLevel: f.indexLevel,
        parentSeqNo: f.parentSeqNo,
        fullIndexName: f.fullIndexName.trim(),
        leadWordStatus: f.leadWordStatus,
      }
      const create = props.kind === 'icd'
        ? knowledgeApi.createIcdIndexEntry
        : knowledgeApi.createCm3IndexEntry
      const update = props.kind === 'icd'
        ? knowledgeApi.updateIcdIndexEntry
        : knowledgeApi.updateCm3IndexEntry
      if (props.entry) await update(props.entry.id, payload)
      else await create({ ...payload, seqNo: f.seqNo })
    }
    toast.success(t('knowledge.indexEntrySaved'))
    emit('saved', f.parentSeqNo)
    emit('update:open', false)
  } catch (error) {
    toast.error(error instanceof ApiError ? error.message : t('knowledge.saveFailed'))
  } finally {
    saving.value = false
  }
}
</script>

<template>
  <Dialog :open="props.open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-xl">
      <DialogHeader>
        <DialogTitle>{{ title }}</DialogTitle>
        <DialogDescription>
          {{ isEdit ? t('knowledge.seqNoLockedHint') : t('knowledge.parentSeqNoHint') }}
        </DialogDescription>
      </DialogHeader>

      <div class="space-y-4">
        <div class="grid grid-cols-3 gap-3">
          <div class="space-y-2">
            <Label>{{ t('knowledge.seqNoColumn') }}</Label>
            <Input v-model.number="form.seqNo" type="number" :disabled="isEdit" />
          </div>
          <div class="space-y-2">
            <Label>{{ t('knowledge.indexLevelColumn') }}</Label>
            <Input v-model.number="form.indexLevel" type="number" min="0" max="9" />
          </div>
          <div class="space-y-2">
            <Label>{{ t('knowledge.parentNodeColumn') }}</Label>
            <Input v-model.number="form.parentSeqNo" type="number" min="0" />
          </div>
        </div>

        <div class="space-y-2">
          <Label>{{ t('knowledge.indexNameColumn') }}</Label>
          <Input v-model="form.indexName" :placeholder="t('knowledge.indexNamePlaceholder')" />
        </div>

        <div v-if="kind === 'tumor'" class="space-y-2">
          <Label>{{ t('knowledge.indexNameEnField') }}</Label>
          <Input v-model="form.indexNameEn" />
        </div>
        <div v-else class="grid grid-cols-2 gap-3">
          <div class="space-y-2">
            <Label>{{ t('knowledge.rawNameColumn') }}</Label>
            <Input v-model="form.rawName" />
          </div>
          <div class="space-y-2">
            <Label>{{ t('knowledge.rawNameEnColumn') }}</Label>
            <Input v-model="form.rawNameEn" />
          </div>
          <div class="space-y-2">
            <Label>{{ t('knowledge.indexCodeColumn') }}</Label>
            <Input v-model="form.indexCode" class="font-mono" />
          </div>
          <div class="space-y-2">
            <Label>{{ t('knowledge.leadWordStatusColumn') }}</Label>
            <Select :model-value="String(form.leadWordStatus)" @update:model-value="form.leadWordStatus = Number($event)">
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="0">{{ t('knowledge.leadWordValid') }}</SelectItem>
                <SelectItem value="1">{{ t('knowledge.leadWordInvalid') }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
        </div>

        <div v-if="kind === 'tumor'" class="space-y-2">
          <Label>{{ t('knowledge.tumorCodesLabel') }}</Label>
          <div class="grid grid-cols-3 gap-3">
            <Input v-model="form.codeMalignantPrimary" class="font-mono" :placeholder="t('knowledge.codeMalignantPrimaryColumn')" />
            <Input v-model="form.codeMalignantSecondary" class="font-mono" :placeholder="t('knowledge.codeMalignantSecondaryColumn')" />
            <Input v-model="form.codeInSitu" class="font-mono" :placeholder="t('knowledge.codeInSituColumn')" />
            <Input v-model="form.codeBenign" class="font-mono" :placeholder="t('knowledge.codeBenignColumn')" />
            <Input v-model="form.codeUncertain" class="font-mono" :placeholder="t('knowledge.codeUncertainColumn')" />
          </div>
        </div>

        <div v-if="kind === 'tumor'" class="grid grid-cols-2 gap-3">
          <div class="space-y-2">
            <Label>{{ t('knowledge.jumpKindColumn') }}</Label>
            <Select :model-value="String(form.jumpKind)" @update:model-value="form.jumpKind = Number($event)">
              <SelectTrigger><SelectValue /></SelectTrigger>
              <SelectContent>
                <SelectItem value="0">{{ t('knowledge.jumpKindNoneOption') }}</SelectItem>
                <SelectItem value="1">{{ t('knowledge.jumpKindSee') }}</SelectItem>
                <SelectItem value="2">{{ t('knowledge.jumpKindSeeAlso') }}</SelectItem>
              </SelectContent>
            </Select>
          </div>
          <div class="space-y-2">
            <Label>{{ t('knowledge.jumpTargetField') }}</Label>
            <Input v-model="form.jumpTarget" :disabled="form.jumpKind === 0" />
          </div>
        </div>

        <div class="space-y-2">
          <Label>{{ t('knowledge.fullIndexNameColumn') }}</Label>
          <Input v-model="form.fullIndexName" :placeholder="t('knowledge.fullIndexNamePlaceholder')" />
        </div>

        <DialogFooter>
          <DialogClose as-child>
            <Button type="button" variant="outline" :disabled="saving">{{ t('common.cancel') }}</Button>
          </DialogClose>
          <Button type="button" :disabled="saving" @click="saveEntry">
            <Loader2 v-if="saving" class="h-4 w-4 mr-2 animate-spin" />
            {{ t('knowledge.save') }}
          </Button>
        </DialogFooter>
      </div>
    </DialogContent>
  </Dialog>
</template>