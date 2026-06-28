<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-lg font-semibold text-gray-800 dark:text-gray-100">Single Sign-On (SSO) Configuration</h2>
      <p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
        Configure tenant-level federated authentication providers.
      </p>
    </div>

    <div
      class="bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/30 rounded-xl p-3 text-xs text-amber-700 dark:text-amber-300"
    >
      LDAP is temporarily disabled for tenant settings.
    </div>

    <div v-if="loading" class="flex items-center gap-3 text-gray-500 dark:text-gray-400 py-8 justify-center">
      <Spinner />
      <span>Loading SSO configuration…</span>
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
          <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
            SSO Provider
          </label>
          <div class="flex flex-wrap gap-2">
            <button
              type="button"
              @click="config.provider = null"
              class="px-4 py-2 rounded-lg text-sm font-medium border transition"
              :class="config.provider === null
                ? 'bg-violet-500 border-violet-500 text-white'
                : 'bg-white dark:bg-gray-800 border-gray-200 dark:border-gray-600 text-gray-600 dark:text-gray-400 hover:border-gray-300 dark:hover:border-gray-500'"
            >
              None
            </button>
            <button
              v-for="p in SSO_PROVIDERS"
              :key="p.id"
              type="button"
              @click="config.provider = p.id"
              class="px-4 py-2 rounded-lg text-sm font-medium border transition"
              :class="config.provider === p.id
                ? 'bg-violet-500 border-violet-500 text-white'
                : 'bg-white dark:bg-gray-800 border-gray-200 dark:border-gray-600 text-gray-600 dark:text-gray-400 hover:border-gray-300 dark:hover:border-gray-500'"
            >
              {{ p.label }}
            </button>
          </div>
          <p class="mt-1.5 text-xs text-gray-400 dark:text-gray-500">
            Select <strong>None</strong> to disable SSO. Users will authenticate with local credentials only.
          </p>
        </div>

        <template v-if="config.provider !== null">
          <div class="pt-2 border-t border-gray-100 dark:border-gray-700/60 space-y-2">
            <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-400 dark:text-gray-500">
              {{ activeProviderLabel }} Settings
            </h3>
            <OIDCConfigForm v-if="config.provider === 'oidc'" v-model="config.oidc" />
            <EntraIDConfigForm v-else-if="config.provider === 'entra'" v-model="config.entra" />
            <GoogleConfigForm v-else-if="config.provider === 'google'" v-model="config.google" />
            <GitHubConfigForm v-else-if="config.provider === 'github'" v-model="config.github" />
          </div>
        </template>

        <div
          v-else
          class="pt-2 border-t border-gray-100 dark:border-gray-700/60"
        >
          <p class="text-sm text-gray-500 dark:text-gray-400 italic">
            SSO authentication is disabled.
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
            :disabled="saving"
            class="btn bg-violet-500 hover:bg-violet-600 text-white px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60 flex items-center gap-2"
          >
            <Spinner v-if="saving" class="w-4 h-4" />
            {{ saving ? 'Saving…' : 'Save' }}
          </button>
          <button
            type="button"
            @click="reset"
            :disabled="saving"
            class="btn bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 text-gray-600 dark:text-gray-400 hover:border-gray-300 dark:hover:border-gray-600 px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60"
          >
            Reset
          </button>
        </div>

        <div
          v-if="serverSnapshot.provider !== null"
          class="pt-4 border-t border-gray-100 dark:border-gray-700/60 space-y-3"
        >
          <div>
            <h3 class="text-sm font-medium text-gray-700 dark:text-gray-300">Test Connection</h3>
            <p class="text-xs text-gray-400 dark:text-gray-500 mt-0.5">
              Verify the saved {{ activeServerProviderLabel }} configuration by fetching the provider metadata endpoint.
            </p>
          </div>
          <div>
            <button
              type="button"
              @click="runTest"
              :disabled="testing"
              class="btn bg-white dark:bg-gray-700 border border-gray-200 dark:border-gray-600 text-gray-700 dark:text-gray-200 hover:border-violet-300 dark:hover:border-violet-500 px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60 flex items-center gap-2"
            >
              <Spinner v-if="testing" class="w-4 h-4" />
              <IconPlugConnected v-else class="w-4 h-4" />
              {{ testing ? 'Testing…' : 'Test Connection' }}
            </button>
          </div>
          <div
            v-if="testResult"
            class="rounded-lg px-4 py-3 text-sm flex items-start gap-2"
            :class="testResult.success
              ? 'bg-green-50 dark:bg-green-500/10 border border-green-200 dark:border-green-500/30 text-green-700 dark:text-green-400'
              : 'bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 text-rose-700 dark:text-rose-400'"
          >
            <IconCircleCheck v-if="testResult.success" class="w-4 h-4 shrink-0 mt-0.5" />
            <IconAlertCircle v-else class="w-4 h-4 shrink-0 mt-0.5" />
            <div>
              <span v-if="testResult.success">
                Connection test passed successfully.
              </span>
              <template v-else>
                <span>Connection test failed.</span>
                <div
                  v-if="testResult.providerError"
                  class="mt-1 font-mono text-xs break-all opacity-80"
                >
                  {{ testResult.providerError }}
                </div>
              </template>
            </div>
          </div>
        </div>

      </div>
    </template>

    <div
      v-if="successMsg"
      class="fixed bottom-4 right-4 z-50 bg-green-500 text-white text-sm font-medium px-4 py-2.5 rounded-xl shadow-lg flex items-center gap-2"
    >
      <IconCircleCheck class="w-4 h-4 shrink-0" />
      {{ successMsg }}
    </div>
  </div>
</template>

<script lang="ts" setup>
import { computed, onMounted, reactive, ref } from 'vue'
import { IconAlertCircle, IconCircleCheck, IconPlugConnected } from '@tabler/icons-vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import OIDCConfigForm from '@/features/instance/features/settings/forms/sso/OIDCConfigForm.vue'
import EntraIDConfigForm from '@/features/instance/features/settings/forms/sso/EntraIDConfigForm.vue'
import GoogleConfigForm from '@/features/instance/features/settings/forms/sso/GoogleConfigForm.vue'
import GitHubConfigForm from '@/features/instance/features/settings/forms/sso/GitHubConfigForm.vue'
import {
  defaultSSOConfig,
  type SSOConfig,
  type SSOProvider,
  type TestSSOResult,
} from '@/api/instance'
import {
  fetchTenantSSOConfig,
  saveTenantSSOConfig,
  testTenantSSOConnection,
} from '@/api/tenants'
import { useTenantStore } from '@/stores/tenant'
import { parseApiValidationError } from '@/utils/apiValidation'

const tenantStore = useTenantStore()

const SSO_PROVIDERS: { id: Exclude<SSOProvider, 'ldap'>; label: string }[] = [
  { id: 'oidc', label: 'Generic OIDC' },
  { id: 'entra', label: 'Entra ID' },
  { id: 'google', label: 'Google' },
  { id: 'github', label: 'GitHub' },
]

const loading = ref(true)
const loadError = ref<string | null>(null)
const saving = ref(false)
const saveError = ref<string | null>(null)
const successMsg = ref<string | null>(null)

const testing = ref(false)
const testResult = ref<TestSSOResult | null>(null)

const config = reactive<SSOConfig>(defaultSSOConfig())
let serverSnapshot: SSOConfig = defaultSSOConfig()

const activeProviderLabel = computed(
  () => SSO_PROVIDERS.find(p => p.id === config.provider)?.label ?? '',
)

const activeServerProviderLabel = computed(
  () => SSO_PROVIDERS.find(p => p.id === serverSnapshot.provider)?.label ?? '',
)

function deepCopyLdap(src: SSOConfig['ldap']): SSOConfig['ldap'] {
  return {
    ...src,
    permissionMapping: {
      instanceAdminGroups: [...src.permissionMapping.instanceAdminGroups],
      tenantAdminMappings: src.permissionMapping.tenantAdminMappings.map(m => ({ ...m })),
      tenantMemberMappings: src.permissionMapping.tenantMemberMappings.map(m => ({ ...m })),
    },
  }
}

function normalizeTenantScopedConfig(raw: SSOConfig): SSOConfig {
  const tenantId = tenantStore.currentTenantId ?? ''
  return {
    ...raw,
    ldap: {
      ...raw.ldap,
      permissionMapping: {
        instanceAdminGroups: [...raw.ldap.permissionMapping.instanceAdminGroups],
        tenantAdminMappings: raw.ldap.permissionMapping.tenantAdminMappings.map(m => ({
          ...m,
          tenantId,
        })),
        tenantMemberMappings: raw.ldap.permissionMapping.tenantMemberMappings.map(m => ({
          ...m,
          tenantId,
        })),
      },
    },
  }
}

function applySnapshot(snap: SSOConfig) {
  config.provider = snap.provider
  config.ldap = deepCopyLdap(snap.ldap)
  config.oidc = { ...snap.oidc }
  config.entra = { ...snap.entra }
  config.google = { ...snap.google }
  config.github = { ...snap.github }
}

function takeSnapshot(): SSOConfig {
  return {
    provider: config.provider,
    ldap: deepCopyLdap(config.ldap),
    oidc: { ...config.oidc },
    entra: { ...config.entra },
    google: { ...config.google },
    github: { ...config.github },
  }
}

function showSuccess(msg: string) {
  successMsg.value = msg
  setTimeout(() => (successMsg.value = null), 3000)
}

async function load() {
  loading.value = true
  loadError.value = null
  try {
    const data = normalizeTenantScopedConfig(await fetchTenantSSOConfig())

    // LDAP is disabled at tenant scope; coerce legacy LDAP provider config to None.
    if (data.provider === 'ldap') {
      data.provider = null
    }

    serverSnapshot = {
      provider: data.provider,
      ldap: deepCopyLdap(data.ldap),
      oidc: { ...data.oidc },
      entra: { ...data.entra },
      google: { ...data.google },
      github: { ...data.github },
    }
    applySnapshot(serverSnapshot)
  } catch (e: any) {
    loadError.value = e.message || 'Failed to load SSO configuration.'
  } finally {
    loading.value = false
  }
}

function reset() {
  applySnapshot(serverSnapshot)
  saveError.value = null
  testResult.value = null
}

async function save() {
  saveError.value = null

  if (config.provider === 'oidc') {
    if (!config.oidc.issuer.trim())       { saveError.value = 'OIDC Issuer is required.'; return }
    if (!config.oidc.clientId.trim())     { saveError.value = 'OIDC Client ID is required.'; return }
    if (!config.oidc.clientSecret.trim()) { saveError.value = 'OIDC Client Secret is required.'; return }
  } else if (config.provider === 'entra') {
    if (!config.entra.tenantId.trim())     { saveError.value = 'Entra Tenant ID is required.'; return }
    if (!config.entra.clientId.trim())     { saveError.value = 'Entra Client ID is required.'; return }
    if (!config.entra.clientSecret.trim()) { saveError.value = 'Entra Client Secret is required.'; return }
  } else if (config.provider === 'google') {
    if (!config.google.clientId.trim())     { saveError.value = 'Google Client ID is required.'; return }
    if (!config.google.clientSecret.trim()) { saveError.value = 'Google Client Secret is required.'; return }
  } else if (config.provider === 'github') {
    if (!config.github.clientId.trim())     { saveError.value = 'GitHub Client ID is required.'; return }
    if (!config.github.clientSecret.trim()) { saveError.value = 'GitHub Client Secret is required.'; return }
  }

  saving.value = true
  try {
    const payload = normalizeTenantScopedConfig(takeSnapshot())
    await saveTenantSSOConfig(payload)
    serverSnapshot = payload
    testResult.value = null
    showSuccess('SSO configuration saved.')
  } catch (e: unknown) {
    const parsed = parseApiValidationError(e, 'Failed to save SSO configuration.')
    saveError.value = parsed.generalError
  } finally {
    saving.value = false
  }
}

async function runTest() {
  testResult.value = null
  testing.value = true
  try {
    testResult.value = await testTenantSSOConnection()
  } catch (e: any) {
    testResult.value = { success: false, providerError: e.message || 'Unexpected error.' }
  } finally {
    testing.value = false
  }
}

onMounted(load)
</script>
