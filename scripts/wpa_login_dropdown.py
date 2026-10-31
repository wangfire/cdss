# -*- coding: utf-8 -*-
"""Login.vue：hospitalId 文本框改下拉（匿名 GET /hospitals/public），保留手动输入回退。"""
import io
import sys

sys.stdout = io.TextIOWrapper(sys.stdout.buffer, encoding="utf-8")

p = r"web-ui\apps\admin\src\views\Login.vue"
c = open(p, encoding="utf-8").read()

# 1. script：加医院列表加载逻辑（onMounted）
anchor_script = "const passwordType = computed(() => (showPassword.value ? 'text' : 'password'))"
add_script = """const passwordType = computed(() => (showPassword.value ? 'text' : 'password'))

/* 医院下拉（WP-A 前端生产化）：匿名公开端点拉取，失败时回退手动输入 GUID。 */
interface HospitalOption {
  id: string
  code: string
  name: string
}
const hospitalOptions = ref<HospitalOption[]>([])
const hospitalsLoading = ref(false)
const manualHospitalInput = ref(false)

async function loadHospitalOptions() {
  if (hospitalsLoading.value || hospitalOptions.value.length) return
  hospitalsLoading.value = true
  try {
    const root = await fetch('/api/v1/hospitals/public').then(r => r.json())
    hospitalOptions.value = (root?.data ?? []) as HospitalOption[]
  }
  catch {
    hospitalOptions.value = []
  }
  finally {
    hospitalsLoading.value = false
    if (hospitalOptions.value.length === 0) manualHospitalInput.value = true
  }
}

function onHospitalSelect(value: string) {
  if (value === '__manual__') {
    manualHospitalInput.value = true
    hospitalId.value = ''
  }
  else {
    hospitalId.value = value
  }
}

void onMounted(async () => {
  await loadHospitalOptions()
})"""
assert anchor_script in c, "script anchor"
c = c.replace(anchor_script, add_script, 1)

# 2. script 导入 onMounted
if "import { ref, computed, onUnmounted } from 'vue'" in c and "onMounted" not in c.split("from 'vue'")[0]:
    c = c.replace(
        "import { ref, computed, onUnmounted } from 'vue'",
        "import { ref, computed, onMounted, onUnmounted } from 'vue'",
        1,
    )

# 3. template：文本框换下拉（含手动输入回退）
old_tpl = """          <div class="space-y-2">
            <Label for="hospitalId">{{ t('login.hospitalId') }}</Label>
            <Input
              id="hospitalId"
              v-model="hospitalId"
              type="text"
              :placeholder="t('login.hospitalIdPlaceholder')"
              required
              autocomplete="off"
              class="h-11 font-mono text-sm"
            />
          </div>"""
new_tpl = """          <div class="space-y-2">
            <Label for="hospitalId">{{ t('login.hospitalId') }}</Label>
            <select
              v-if="!manualHospitalInput && hospitalOptions.length"
              id="hospitalId"
              :value="hospitalId"
              required
              autocomplete="off"
              class="flex h-11 w-full rounded-md border border-input bg-background px-3 py-2 text-sm"
              @change="onHospitalSelect(($event.target as HTMLSelectElement).value)"
            >
              <option value="" disabled>{{ t('login.hospitalSelectPlaceholder') }}</option>
              <option v-for="h in hospitalOptions" :key="h.id" :value="h.id">
                {{ h.name }}（{{ h.code }}）
              </option>
              <option value="__manual__">{{ t('login.hospitalManualInput') }}</option>
            </select>
            <Input
              v-else
              id="hospitalId"
              v-model="hospitalId"
              type="text"
              :placeholder="t('login.hospitalIdPlaceholder')"
              required
              autocomplete="off"
              class="h-11 font-mono text-sm"
            />
          </div>"""
assert old_tpl in c, "template anchor"
c = c.replace(old_tpl, new_tpl, 1)

open(p, "w", encoding="utf-8").write(c)
print("Login.vue hospital dropdown added")
