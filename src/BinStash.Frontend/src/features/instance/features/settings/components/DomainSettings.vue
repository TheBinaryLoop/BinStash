<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-lg font-semibold text-gray-800 dark:text-gray-100">Instance URL &amp; Domain</h2>
      <p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
        Configure the public base URL used when generating links in emails and redirects.
      </p>
    </div>

    <div v-if="loading" class="flex items-center gap-3 text-gray-500 dark:text-gray-400 py-8 justify-center">
      <Spinner />
      <span>Loading domain settings…</span>
    </div>

    <div
      v-else-if="loadError"
      class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-4 text-sm text-rose-700 dark:text-rose-400"
    >
      {{ loadError }}
    </div>

    <template v-else>
      <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-6 border border-gray-100 dark:border-gray-700/60 space-y-6">
        <div>
          <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1" for="instance-base-url">
            Public Base URL
          </label>
          <input
            id="instance-base-url"
            v-model.trim="form.baseUrl"
            type="url"
            placeholder="https://app.example.com"
            class="form-input w-full"
            :class="fieldErrors.baseUrl ? 'border-rose-300 dark:border-rose-500/60 focus:border-rose-400 focus:ring-rose-200' : ''"
          />
          <p v-if="fieldErrors.baseUrl" class="mt-1.5 text-xs text-rose-600 dark:text-rose-400">
            {{ fieldErrors.baseUrl }}
          </p>
          <p class="mt-1.5 text-xs text-gray-400 dark:text-gray-500">
            Must include <strong>http://</strong> or <strong>https://</strong>. This value is shared across the whole instance.
          </p>
        </div>

        <div
          v-if="saveError"
          class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-3 text-sm text-rose-700 dark:text-rose-400"
        >
          {{ saveError }}
        </div>

        <div class="flex items-center gap-3 pt-2 border-t border-gray-100 dark:border-gray-700/60">
          <button
            type="button"
            @click="save"
            :disabled="saving || !isDirty"
            class="btn bg-violet-500 hover:bg-violet-600 text-white px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60 flex items-center gap-2"
          >
            <Spinner v-if="saving" class="w-4 h-4" />
            {{ saving ? 'Saving…' : 'Save' }}
          </button>
          <button
            type="button"
            @click="reset"
            :disabled="saving || !isDirty"
            class="btn bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 text-gray-600 dark:text-gray-400 hover:border-gray-300 dark:hover:border-gray-600 px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60"
          >
            Reset
          </button>
        </div>
      </div>
    </template>

    <div
      v-if="successMsg"
      class="fixed bottom-4 right-4 z-50 bg-green-500 text-white text-sm font-medium px-4 py-2.5 rounded-xl shadow-lg"
    >
      {{ successMsg }}
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import {
  fetchInstanceDomainConfig,
  saveInstanceDomainConfig,
  type InstanceDomainConfig,
} from '@/api/instance'
import { findFieldError, parseApiValidationError } from '@/utils/apiValidation'

const loading = ref(true)
const loadError = ref<string | null>(null)
const saving = ref(false)
const saveError = ref<string | null>(null)
const successMsg = ref<string | null>(null)
const fieldErrors = reactive<{ baseUrl: string | null }>({ baseUrl: null })

const form = reactive<InstanceDomainConfig>({ baseUrl: '' })
let serverSnapshot: InstanceDomainConfig = { baseUrl: '' }

const isDirty = computed(() => form.baseUrl !== serverSnapshot.baseUrl)

function applySnapshot(snap: InstanceDomainConfig) {
  form.baseUrl = snap.baseUrl
}

function reset() {
  applySnapshot(serverSnapshot)
  saveError.value = null
  fieldErrors.baseUrl = null
}

function normalizeUrl(value: string): string {
  return value.trim().replace(/\/+$/, '')
}

function validateUrl(value: string): string | null {
  if (!value) return 'Base URL is required.'
  let parsed: URL
  try {
    parsed = new URL(value)
  } catch {
    return 'Please enter a valid absolute URL (for example: https://app.example.com).'
  }
  if (!['http:', 'https:'].includes(parsed.protocol)) {
    return 'Base URL must start with http:// or https://.'
  }
  return null
}

async function load() {
  loading.value = true
  loadError.value = null
  try {
    const cfg = await fetchInstanceDomainConfig()
    serverSnapshot = { baseUrl: normalizeUrl(cfg.baseUrl ?? '') }
    applySnapshot(serverSnapshot)
  } catch (e: any) {
    loadError.value = e?.message || 'Failed to load domain settings.'
  } finally {
    loading.value = false
  }
}

function showSuccess(msg: string) {
  successMsg.value = msg
  setTimeout(() => (successMsg.value = null), 3000)
}

async function save() {
  saveError.value = null
  fieldErrors.baseUrl = null
  const normalized = normalizeUrl(form.baseUrl)
  const validationError = validateUrl(normalized)
  if (validationError) {
    fieldErrors.baseUrl = validationError
    saveError.value = 'Please correct the highlighted fields.'
    return
  }

  saving.value = true
  try {
    await saveInstanceDomainConfig({ baseUrl: normalized })
    serverSnapshot = { baseUrl: normalized }
    applySnapshot(serverSnapshot)
    showSuccess('Instance base URL saved.')
  } catch (e: unknown) {
    const parsed = parseApiValidationError(e, 'Failed to save domain settings.')
    saveError.value = parsed.generalError
    fieldErrors.baseUrl = findFieldError(parsed.fieldErrors, 'baseUrl', 'baseurl')
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>
