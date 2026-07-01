<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-lg font-semibold text-ink-strong">Instance URL &amp; Domain</h2>
      <p class="mt-0.5 text-sm text-ink-muted">
        Configure the public base URL used when generating links in emails and redirects.
      </p>
    </div>

    <div v-if="loading" class="flex items-center justify-center gap-3 py-8 text-ink-muted">
      <Spinner :size="20" color="var(--color-accent)" />
      <span>Loading domain settings…</span>
    </div>

    <div
      v-else-if="loadError"
      class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
    >
      {{ loadError }}
    </div>

    <template v-else>
      <BaseCard>
        <div class="space-y-6">
          <BaseInput
            v-model.trim="form.baseUrl"
            type="url"
            label="Public Base URL"
            placeholder="https://app.example.com"
            :error="fieldErrors.baseUrl ?? undefined"
            hint="Must include http:// or https://. This value is shared across the whole instance."
          />

          <div
            v-if="saveError"
            class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
          >
            {{ saveError }}
          </div>

          <div class="flex items-center gap-3 border-t border-hairline pt-4">
            <BaseButton :loading="saving" :disabled="saving || !isDirty" @click="save">
              {{ saving ? 'Saving…' : 'Save' }}
            </BaseButton>
            <BaseButton variant="secondary" :disabled="saving || !isDirty" @click="reset">
              Reset
            </BaseButton>
          </div>
        </div>
      </BaseCard>
    </template>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, reactive, ref } from 'vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BaseButton, BaseCard, BaseInput } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'
import {
  fetchInstanceDomainConfig,
  saveInstanceDomainConfig,
  type InstanceDomainConfig,
} from '@/api/instance'
import { findFieldError, parseApiValidationError } from '@/utils/apiValidation'

const toast = useToast()

const loading = ref(true)
const loadError = ref<string | null>(null)
const saving = ref(false)
const saveError = ref<string | null>(null)
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
    toast.success('Instance base URL saved.')
  } catch (e: unknown) {
    const parsed = parseApiValidationError(e, 'Failed to save domain settings.')
    saveError.value = parsed.generalError
    fieldErrors.baseUrl = findFieldError(parsed.fieldErrors, 'baseUrl', 'baseurl')
    toast.error(parsed.generalError || 'Failed to save domain settings.')
  } finally {
    saving.value = false
  }
}

onMounted(load)
</script>
