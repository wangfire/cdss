<script setup lang="ts">
import { Loader2, Search } from 'lucide-vue-next'
import { ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import { ApiError, knowledgeApi } from '@/api'
import type { MedicalCodeDto } from '@/api'
import {
  Badge,
  Button,
  Dialog,
  DialogClose,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
  Input,
  toast,
} from '@tabtab/ui'

/**
 * 字典表选择弹窗：按编码/名称关键字检索 medical_code（ICD-10 / ICD-9-CM-3），
 * 选中即回填编码录入条目（字典直达）。
 */
const props = defineProps<{
  open: boolean
  kind: 'diagnosis' | 'procedure'
}>()

const emit = defineEmits<{
  (event: 'update:open', value: boolean): void
  (event: 'picked', code: MedicalCodeDto): void
}>()

const { t } = useI18n()

const keyword = ref('')
const loading = ref(false)
const rows = ref<MedicalCodeDto[]>([])
const total = ref(0)
const page = ref(1)
const pageSize = 20

const codeSystem = () => (props.kind === 'procedure' ? 'ICD-9-CM-3' : 'ICD-10')

async function load() {
  loading.value = true
  try {
    const resp = await knowledgeApi.getMedicalCodes({
      codeSystem: codeSystem(),
      keyword: keyword.value.trim() || undefined,
      page: page.value,
      pageSize,
    })
    rows.value = resp.items
    total.value = resp.total
  } catch (error) {
    toast.error(error instanceof ApiError ? error.message : t('codingEntry.dictionaryLoadFailed'))
  } finally {
    loading.value = false
  }
}

watch(
  () => props.open,
  (open) => {
    if (open) {
      keyword.value = ''
      page.value = 1
      void load()
    }
  },
)

function search() {
  page.value = 1
  void load()
}

function pick(row: MedicalCodeDto) {
  emit('picked', row)
  emit('update:open', false)
}

const totalPages = () => Math.max(1, Math.ceil(total.value / pageSize))

function gotoPage(next: number) {
  if (next < 1 || next > totalPages()) return
  page.value = next
  void load()
}
</script>

<template>
  <Dialog :open="props.open" @update:open="emit('update:open', $event)">
    <DialogContent class="sm:max-w-2xl">
      <DialogHeader>
        <DialogTitle>{{ t('codingEntry.dictionaryPickerTitle') }}</DialogTitle>
        <DialogDescription>
          {{ kind === 'procedure' ? t('codingEntry.dictionaryCm3Hint') : t('codingEntry.dictionaryIcdHint') }}
        </DialogDescription>
      </DialogHeader>

      <div class="flex gap-2">
        <Input
          v-model="keyword"
          :placeholder="t('codingEntry.dictionarySearchPlaceholder')"
          @keyup.enter="search"
        />
        <Button type="button" variant="outline" :disabled="loading" @click="search">
          <Loader2 v-if="loading" class="h-4 w-4 mr-1 animate-spin" />
          <Search v-else class="h-4 w-4 mr-1" />
          {{ t('codingEntry.search') }}
        </Button>
      </div>

      <div class="max-h-80 overflow-y-auto rounded-md border">
        <table class="w-full text-sm">
          <thead class="sticky top-0 bg-muted">
            <tr>
              <th class="px-3 py-2 text-left font-medium w-36">{{ t('codingEntry.dictionaryCodeColumn') }}</th>
              <th class="px-3 py-2 text-left font-medium">{{ t('codingEntry.dictionaryTitleColumn') }}</th>
              <th class="px-3 py-2 w-16"></th>
            </tr>
          </thead>
          <tbody>
            <tr v-if="rows.length === 0 && !loading">
              <td colspan="3" class="px-3 py-6 text-center text-muted-foreground">
                {{ t('codingEntry.dictionaryEmpty') }}
              </td>
            </tr>
            <tr v-for="row in rows" :key="row.id" class="border-t hover:bg-muted/50">
              <td class="px-3 py-2 font-mono">{{ row.code }}</td>
              <td class="px-3 py-2">{{ row.title }}</td>
              <td class="px-3 py-2 text-right">
                <Badge
                  v-if="!row.isEnabled"
                  variant="secondary"
                >{{ t('codingEntry.dictionaryDisabled') }}</Badge>
                <Button v-else type="button" size="sm" variant="outline" @click="pick(row)">
                  {{ t('codingEntry.dictionaryPick') }}
                </Button>
              </td>
            </tr>
          </tbody>
        </table>
      </div>

      <DialogFooter class="items-center justify-between sm:justify-between">
        <span class="text-sm text-muted-foreground">
          {{ t('codingEntry.dictionaryTotal', { total }) }}
        </span>
        <div class="flex gap-2">
          <Button type="button" size="sm" variant="outline" :disabled="page <= 1" @click="gotoPage(page - 1)">
            {{ t('codingEntry.prevPage') }}
          </Button>
          <span class="px-2 py-1 text-sm">{{ page }} / {{ totalPages() }}</span>
          <Button
            type="button"
            size="sm"
            variant="outline"
            :disabled="page >= totalPages()"
            @click="gotoPage(page + 1)"
          >
            {{ t('codingEntry.nextPage') }}
          </Button>
          <DialogClose as-child>
            <Button type="button" variant="ghost">{{ t('common.close') }}</Button>
          </DialogClose>
        </div>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
