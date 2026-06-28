<template>
  <div class="space-y-6">

    <!-- Loading -->
    <div v-if="loading" class="flex justify-center py-10">
      <div class="animate-spin rounded-full h-7 w-7 border-b-2 border-violet-500"></div>
    </div>

    <!-- Load error -->
    <div
      v-else-if="loadError"
      class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-xl p-4 text-sm text-red-700 dark:text-red-400"
    >
      {{ loadError }}
    </div>

    <!-- Form -->
    <template v-else>

      <!-- Section title -->
      <div>
        <h3 class="text-base font-semibold text-gray-800 dark:text-gray-100">Tenancy Configuration</h3>
        <p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
          Configure whether this instance operates in single-tenant or multi-tenant mode.
        </p>
      </div>

      <!-- Tenancy mode cards -->
      <div>
        <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-3">
          Tenancy Mode
        </label>
        <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">

          <!-- Single tenant -->
          <button
            type="button"
            @click="form.mode = 'Single'"
            class="text-left rounded-xl border-2 p-4 transition-all"
            :class="form.mode === 'Single'
              ? 'border-violet-500 bg-violet-50 dark:bg-violet-500/10'
              : 'border-gray-200 dark:border-gray-700 hover:border-violet-300 dark:hover:border-violet-600'"
          >
            <div class="flex items-center gap-3 mb-2">
              <div
                class="w-9 h-9 rounded-lg flex items-center justify-center shrink-0"
                :class="form.mode === 'Single' ? 'bg-violet-100 dark:bg-violet-500/20' : 'bg-gray-100 dark:bg-gray-700'"
              >
                <IconUser
                  class="w-5 h-5"
                  :class="form.mode === 'Single' ? 'text-violet-600 dark:text-violet-400' : 'text-gray-400'"
                />
              </div>
              <div class="flex items-center gap-2">
                <span class="font-semibold text-sm" :class="form.mode === 'Single' ? 'text-violet-700 dark:text-violet-300' : 'text-gray-700 dark:text-gray-300'">Single Tenant</span>
                <div
                  v-if="form.mode === 'Single'"
                  class="w-4 h-4 rounded-full bg-violet-500 flex items-center justify-center shrink-0"
                >
                  <IconCheck class="w-2.5 h-2.5 text-white" />
                </div>
              </div>
            </div>
            <p class="text-xs text-gray-500 dark:text-gray-400 leading-relaxed">
              The instance serves a single organisation. All users belong to the same tenant
              automatically. Ideal for self-hosted, single-team deployments.
            </p>
          </button>

          <!-- Multi tenant -->
          <button
            type="button"
            @click="form.mode = 'Multi'"
            class="text-left rounded-xl border-2 p-4 transition-all"
            :class="form.mode === 'Multi'
              ? 'border-violet-500 bg-violet-50 dark:bg-violet-500/10'
              : 'border-gray-200 dark:border-gray-700 hover:border-violet-300 dark:hover:border-violet-600'"
          >
            <div class="flex items-center gap-3 mb-2">
              <div
                class="w-9 h-9 rounded-lg flex items-center justify-center shrink-0"
                :class="form.mode === 'Multi' ? 'bg-violet-100 dark:bg-violet-500/20' : 'bg-gray-100 dark:bg-gray-700'"
              >
                <IconBuildingSkyscraper
                  class="w-5 h-5"
                  :class="form.mode === 'Multi' ? 'text-violet-600 dark:text-violet-400' : 'text-gray-400'"
                />
              </div>
              <div class="flex items-center gap-2">
                <span class="font-semibold text-sm" :class="form.mode === 'Multi' ? 'text-violet-700 dark:text-violet-300' : 'text-gray-700 dark:text-gray-300'">Multi Tenant</span>
                <div
                  v-if="form.mode === 'Multi'"
                  class="w-4 h-4 rounded-full bg-violet-500 flex items-center justify-center shrink-0"
                >
                  <IconCheck class="w-2.5 h-2.5 text-white" />
                </div>
              </div>
            </div>
            <p class="text-xs text-gray-500 dark:text-gray-400 leading-relaxed">
              The instance supports multiple independent organisations. Each tenant has its own
              repositories, members, and settings. Users must select a tenant on login.
            </p>
          </button>

        </div>
      </div>

      <!-- Default tenant selector — only shown in Single mode -->
      <Transition name="fade-slide">
        <div v-if="form.mode === 'Single'" class="space-y-2">
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300">
            Default Tenant
          </label>
          <p class="text-xs text-gray-500 dark:text-gray-400">
            In single-tenant mode all requests are automatically routed to this tenant.
          </p>

          <!-- Tenant list loading -->
          <div v-if="tenantsLoading" class="flex items-center gap-2 text-sm text-gray-400 py-2">
            <div class="animate-spin rounded-full h-4 w-4 border-b-2 border-violet-500"></div>
            Loading tenants…
          </div>

          <!-- No tenants -->
          <div
            v-else-if="tenants.length === 0"
            class="text-sm text-amber-600 dark:text-amber-400 bg-amber-50 dark:bg-amber-900/20 border border-amber-200 dark:border-amber-800 rounded-lg px-3 py-2"
          >
            No tenants found. <router-link to="/instance/tenants" class="underline hover:no-underline">Create a tenant</router-link> first.
          </div>

          <!-- Dropdown -->
          <select
            v-else
            v-model="form.defaultTenantId"
            class="form-select w-full max-w-sm text-sm"
            :class="fieldErrors.defaultTenantId ? 'border-rose-300 dark:border-rose-500/60 focus:border-rose-400 focus:ring-rose-200' : ''"
          >
            <option value="" disabled>— select a tenant —</option>
            <option
              v-for="t in tenants"
              :key="t.tenantId"
              :value="t.tenantId"
            >
              {{ t.name }}  ({{ t.slug }})
            </option>
          </select>
          <p v-if="fieldErrors.defaultTenantId" class="text-xs text-rose-600 dark:text-rose-400">
            {{ fieldErrors.defaultTenantId }}
          </p>
        </div>
      </Transition>

      <!-- Divider -->
      <hr class="border-gray-200 dark:border-gray-700/60" />

      <!-- Save / cancel -->
      <div class="flex items-center justify-between">
        <div class="text-sm">
          <span
            v-if="saveSuccess"
            class="text-green-600 dark:text-green-400 font-medium"
          >
            ✓ Settings saved.
          </span>
          <span v-if="saveError" class="text-red-500">{{ saveError }}</span>
        </div>
        <div class="flex items-center gap-3">
          <button
            type="button"
            @click="resetForm"
            :disabled="saveBusy"
            class="btn border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700 disabled:opacity-50"
          >
            Reset
          </button>
          <button
            type="button"
            @click="save"
            :disabled="saveBusy || !isDirty"
            class="btn bg-violet-500 hover:bg-violet-600 text-white disabled:opacity-50"
          >
            {{ saveBusy ? 'Saving…' : 'Save Changes' }}
          </button>
        </div>
      </div>

    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, reactive, computed, watch, onMounted } from 'vue'
import { IconUser, IconBuildingSkyscraper, IconCheck } from '@tabler/icons-vue'
import { fetchTenancyConfig, saveTenancyConfig } from '@/api/instance'
import { listTenantsForMember } from '@/api/tenants'
import { findFieldError, parseApiValidationError } from '@/utils/apiValidation'
import type { TenantSummaryDto } from '@/stores/tenant'
import type { TenancyMode } from '@/api/instance'

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
    await loadConfig() // reload to ensure we have the latest saved config (and to handle any server-side adjustments)
  } catch (e: unknown) {
    const parsed = parseApiValidationError(e, 'Failed to save tenancy configuration.')
    saveError.value = parsed.generalError
    fieldErrors.defaultTenantId = findFieldError(parsed.fieldErrors, 'defaultTenantId', 'defaulttenantid')
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
