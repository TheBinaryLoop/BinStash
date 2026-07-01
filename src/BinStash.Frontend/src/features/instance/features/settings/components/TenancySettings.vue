<template>
  <div class="space-y-6">

    <!-- Loading -->
    <div v-if="loading" class="flex justify-center py-10">
      <Spinner :size="28" color="var(--color-accent)" />
    </div>

    <!-- Load error -->
    <div
      v-else-if="loadError"
      class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
    >
      {{ loadError }}
    </div>

    <!-- Form -->
    <template v-else>

      <!-- Section title -->
      <div>
        <h3 class="text-base font-semibold text-ink-strong">Tenancy Configuration</h3>
        <p class="mt-0.5 text-sm text-ink-muted">
          Configure whether this instance operates in single-tenant or multi-tenant mode.
        </p>
      </div>

      <!-- Tenancy mode cards -->
      <div>
        <label class="mb-3 block text-sm font-medium text-ink-strong">
          Tenancy Mode
        </label>
        <BaseRadioGroup
          v-model="form.mode"
          :columns="2"
          :options="modeOptions"
        />
      </div>

      <!-- Default tenant selector — only shown in Single mode -->
      <Transition name="fade-slide">
        <div v-if="form.mode === 'Single'" class="space-y-2">
          <div>
            <label class="block text-sm font-medium text-ink-strong">
              Default Tenant
            </label>
            <p class="mt-0.5 text-xs text-ink-muted">
              In single-tenant mode all requests are automatically routed to this tenant.
            </p>
          </div>

          <!-- Tenant list loading -->
          <div v-if="tenantsLoading" class="flex items-center gap-2 py-2 text-sm text-ink-subtle">
            <Spinner :size="16" color="var(--color-accent)" />
            Loading tenants…
          </div>

          <!-- No tenants -->
          <div
            v-else-if="tenants.length === 0"
            class="rounded-control border border-warning/25 bg-warning-soft px-3 py-2 text-sm text-warning"
          >
            No tenants found. <router-link to="/instance/tenants" class="underline hover:no-underline">Create a tenant</router-link> first.
          </div>

          <!-- Dropdown -->
          <BaseSelect
            v-else
            v-model="form.defaultTenantId"
            class="max-w-sm"
            placeholder="— select a tenant —"
            :error="fieldErrors.defaultTenantId ?? undefined"
          >
            <option value="" disabled>— select a tenant —</option>
            <option
              v-for="t in tenants"
              :key="t.tenantId"
              :value="t.tenantId"
            >
              {{ t.name }}  ({{ t.slug }})
            </option>
          </BaseSelect>
        </div>
      </Transition>

      <!-- Divider -->
      <hr class="border-hairline" />

      <!-- Save / cancel -->
      <div class="flex items-center justify-between">
        <div class="text-sm">
          <span v-if="saveSuccess" class="font-medium text-success">
            ✓ Settings saved.
          </span>
          <span v-if="saveError" class="text-danger">{{ saveError }}</span>
        </div>
        <div class="flex items-center gap-3">
          <BaseButton variant="secondary" :disabled="saveBusy" @click="resetForm">
            Reset
          </BaseButton>
          <BaseButton :loading="saveBusy" :disabled="saveBusy || !isDirty" @click="save">
            {{ saveBusy ? 'Saving…' : 'Save Changes' }}
          </BaseButton>
        </div>
      </div>

    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, watch, onMounted } from 'vue'
import { IconUser, IconBuildingSkyscraper } from '@tabler/icons-vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BaseButton, BaseSelect, BaseRadioGroup, type RadioOption } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'
import { fetchTenancyConfig, saveTenancyConfig } from '@/api/instance'
import { listTenantsForMember } from '@/api/tenants'
import { findFieldError, parseApiValidationError } from '@/utils/apiValidation'
import type { TenantSummaryDto } from '@/stores/tenant'
import type { TenancyMode } from '@/api/instance'

const toast = useToast()

// ── State ─────────────────────────────────────────────────────────────────────

const loading = ref(true)
const loadError = ref<string | null>(null)

const tenantsLoading = ref(false)
const tenants = ref<TenantSummaryDto[]>([])

const form = reactive<{ mode: TenancyMode; defaultTenantId: string | null }>({
  mode: 'Multi',
  defaultTenantId: null,
})
// Snapshot used to detect dirty state
let saved = { mode: 'Multi' as TenancyMode, defaultTenantId: null as string | null }

const isDirty = computed(() =>
  form.mode !== saved.mode || form.defaultTenantId !== saved.defaultTenantId,
)

const saveBusy = ref(false)
const saveSuccess = ref(false)
const saveError = ref<string | null>(null)
const fieldErrors = reactive<{ defaultTenantId: string | null }>({
  defaultTenantId: null,
})

const modeOptions: RadioOption[] = [
  {
    value: 'Single',
    label: 'Single Tenant',
    description:
      'The instance serves a single organisation. All users belong to the same tenant automatically. Ideal for self-hosted, single-team deployments.',
    icon: IconUser,
  },
  {
    value: 'Multi',
    label: 'Multi Tenant',
    description:
      'The instance supports multiple independent organisations. Each tenant has its own repositories, members, and settings. Users must select a tenant on login.',
    icon: IconBuildingSkyscraper,
  },
]

// ── Load ──────────────────────────────────────────────────────────────────────

async function loadConfig() {
  loading.value = true
  loadError.value = null
  try {
    const config = await fetchTenancyConfig()
    form.mode = config.mode
    form.defaultTenantId = config.defaultTenantId
    saved = { mode: config.mode, defaultTenantId: config.defaultTenantId ?? null }
  } catch (e: any) {
    loadError.value = e?.message ?? 'Failed to load tenancy configuration.'
  } finally {
    loading.value = false
  }
}

async function loadTenants() {
  tenantsLoading.value = true
  try {
    tenants.value = await listTenantsForMember()
  } catch {
    tenants.value = []
  } finally {
    tenantsLoading.value = false
  }
}

// Load tenants whenever Single mode is active
watch(() => form.mode, (mode) => {
  if (mode === 'Single' && tenants.value.length === 0) loadTenants()
})

// ── Save ──────────────────────────────────────────────────────────────────────

async function save() {
  saveError.value = null
  saveSuccess.value = false
  fieldErrors.defaultTenantId = null

  if (form.mode === 'Single' && !form.defaultTenantId) {
    fieldErrors.defaultTenantId = 'Please select a default tenant for single-tenant mode.'
    saveError.value = 'Please correct the highlighted fields.'
    return
  }

  saveBusy.value = true
  try {
    await saveTenancyConfig({
      mode: form.mode,
      defaultTenantId: form.mode === 'Single' ? form.defaultTenantId : undefined,
    })
    saved = { mode: form.mode, defaultTenantId: form.defaultTenantId }
    saveSuccess.value = true
    setTimeout(() => { saveSuccess.value = false }, 3000)
    toast.success('Tenancy configuration saved.')
    await loadConfig() // reload to ensure we have the latest saved config (and to handle any server-side adjustments)
  } catch (e: unknown) {
    const parsed = parseApiValidationError(e, 'Failed to save tenancy configuration.')
    saveError.value = parsed.generalError
    fieldErrors.defaultTenantId = findFieldError(parsed.fieldErrors, 'defaultTenantId', 'defaulttenantid')
    toast.error(parsed.generalError || 'Failed to save tenancy configuration.')
  } finally {
    saveBusy.value = false
  }
}

function resetForm() {
  form.mode = saved.mode
  form.defaultTenantId = saved.defaultTenantId
  saveError.value = null
  saveSuccess.value = false
  fieldErrors.defaultTenantId = null
}

// ── Init ──────────────────────────────────────────────────────────────────────

onMounted(async () => {
  await loadConfig()
  if (form.mode === 'Single') loadTenants()
})
</script>

<style scoped>
.fade-slide-enter-active,
.fade-slide-leave-active {
  transition: opacity 0.2s ease, transform 0.2s ease;
}
.fade-slide-enter-from,
.fade-slide-leave-to {
  opacity: 0;
  transform: translateY(-6px);
}
</style>
