<template>
  <div class="space-y-6">
    <!-- Section header -->
    <div>
      <h2 class="text-lg font-semibold text-gray-800 dark:text-gray-100">Single Sign-On (SSO) Configuration</h2>
      <p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
        Configure a federated authentication provider. Users will be able to sign in using the selected provider.
      </p>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center gap-3 text-gray-500 dark:text-gray-400 py-8 justify-center">
      <Spinner />
      <span>Loading SSO configuration…</span>
    </div>

    <!-- Load error -->
    <div
      v-else-if="loadError"
      class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-4 text-sm text-rose-700 dark:text-rose-400"
    >
      {{ loadError }}
    </div>

    <template v-else>
      <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-6 border border-gray-100 dark:border-gray-700/60 space-y-6">

        <!-- Provider selector -->
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
          <!-- Provider-specific config -->
          <div class="pt-2 border-t border-gray-100 dark:border-gray-700/60 space-y-2">
            <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-400 dark:text-gray-500">
              {{ activeProviderLabel }} Settings
            </h3>
            <LDAPConfigForm
              v-if="config.provider === 'ldap'"
              v-model="config.ldap"
              :tenancyMode="tenancyMode"
              :tenants="tenants"
            />
            <OIDCConfigForm   v-else-if="config.provider === 'oidc'"   v-model="config.oidc" />
            <EntraIDConfigForm v-else-if="config.provider === 'entra'" v-model="config.entra" />
            <GoogleConfigForm v-else-if="config.provider === 'google'" v-model="config.google" />
            <GitHubConfigForm v-else-if="config.provider === 'github'" v-model="config.github" />
          </div>
        </template>

        <!-- No-provider notice -->
        <div
          v-else
          class="pt-2 border-t border-gray-100 dark:border-gray-700/60"
        >
          <p class="text-sm text-gray-500 dark:text-gray-400 italic">
            SSO authentication is disabled.
          </p>
        </div>

        <!-- Save error -->
        <div
          v-if="saveError"
          class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-3 text-sm text-rose-700 dark:text-rose-400"
        >
          {{ saveError }}
        </div>

        <!-- Actions -->
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

        <!-- ── Test Connection ─────────────────────────────────────────── -->
        <div
          v-if="serverSnapshot.provider !== null"
          class="pt-4 border-t border-gray-100 dark:border-gray-700/60 space-y-3"
        >
          <div>
            <h3 class="text-sm font-medium text-gray-700 dark:text-gray-300">Test Connection</h3>
            <p class="text-xs text-gray-400 dark:text-gray-500 mt-0.5">
              <template v-if="serverSnapshot.provider === 'ldap'">
                Verify the saved LDAP configuration by performing a test bind against the directory server.
              </template>
              <template v-else>
                Verify the saved {{ activeServerProviderLabel }} configuration by fetching the provider's
                discovery / metadata endpoint.
              </template>
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
          <!-- Test result -->
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

    <!-- Success toast -->
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
import { ref, reactive, computed, onMounted } from 'vue'
import { IconCircleCheck, IconAlertCircle, IconPlugConnected } from '@tabler/icons-vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import LDAPConfigForm from '@/features/instance/features/settings/forms/sso/LDAPConfigForm.vue'
import OIDCConfigForm from '@/features/instance/features/settings/forms/sso/OIDCConfigForm.vue'
import EntraIDConfigForm from '@/features/instance/features/settings/forms/sso/EntraIDConfigForm.vue'
import GoogleConfigForm from '@/features/instance/features/settings/forms/sso/GoogleConfigForm.vue'
import GitHubConfigForm from '@/features/instance/features/settings/forms/sso/GitHubConfigForm.vue'
import {
  fetchSSOConfig,
  saveSSOConfig,
  testSSOConnection,
  defaultSSOConfig,
  fetchTenancyConfig,
  type SSOProvider,
  type SSOConfig,
  type TenancyMode,
  type TestSSOResult,
} from '@/api/instance'
import { parseApiValidationError } from '@/utils/apiValidation'
import { listTenantsForMember } from '@/api/tenants'
import type { TenantSummaryDto } from '@/stores/tenant'

// ── Provider registry ─────────────────────────────────────────────────────────
// To add a new provider:
//   1. Add its config type to instance.ts and extend SSOConfig / defaultSSOConfig.
//   2. Create XxxConfigForm.vue in ./sso/ (see LDAPConfigForm.vue as a reference).
//   3. Add an entry here and a v-if branch in the template above.

const SSO_PROVIDERS: { id: SSOProvider; label: string }[] = [
  { id: 'ldap',   label: 'LDAP' },
  { id: 'oidc',   label: 'Generic OIDC' },
  { id: 'entra',  label: 'Entra ID' },
  { id: 'google', label: 'Google' },
  { id: 'github', label: 'GitHub' },
]

// ── State ─────────────────────────────────────────────────────────────────────

const loading    = ref(true)
const loadError  = ref<string | null>(null)
const saving     = ref(false)
const saveError  = ref<string | null>(null)
const successMsg = ref<string | null>(null)

const testing    = ref(false)
const testResult = ref<TestSSOResult | null>(null)

/** Tenancy context — loaded alongside SSO config and passed to provider sub-forms. */
const tenancyMode = ref<TenancyMode>('Multi')
const tenants     = ref<TenantSummaryDto[]>([])

/** Live form state — bound directly to sub-forms via v-model. */
const config = reactive<SSOConfig>(defaultSSOConfig())

/**
 * Snapshot of the last server-saved state.
 * Used for Reset and to decide whether to show the Test Connection section
 * (we only test the actual saved config, not unsaved draft changes).
 */
let serverSnapshot: SSOConfig = defaultSSOConfig()

// ── Derived ───────────────────────────────────────────────────────────────────

const activeProviderLabel = computed(
  () => SSO_PROVIDERS.find(p => p.id === config.provider)?.label ?? '',
)

/** Label for the *saved* provider — used in the test block description. */
const activeServerProviderLabel = computed(
  () => SSO_PROVIDERS.find(p => p.id === serverSnapshot.provider)?.label ?? '',
)

// ── Helpers ───────────────────────────────────────────────────────────────────

/** Deep-copies the LDAP config so nested permissionMapping arrays are independent. */
function deepCopyLdap(src: SSOConfig['ldap']): SSOConfig['ldap'] {
  return {
    ...src,
    permissionMapping: {
      instanceAdminGroups: [...src.permissionMapping.instanceAdminGroups],
      tenantAdminMappings:  src.permissionMapping.tenantAdminMappings.map(m => ({ ...m })),
      tenantMemberMappings: src.permissionMapping.tenantMemberMappings.map(m => ({ ...m })),
    },
  }
}

function applySnapshot(snap: SSOConfig) {
  config.provider = snap.provider
  config.ldap   = deepCopyLdap(snap.ldap)
  config.oidc   = { ...snap.oidc }
  config.entra  = { ...snap.entra }
  config.google = { ...snap.google }
  config.github = { ...snap.github }
}

function takeSnapshot(): SSOConfig {
  return {
    provider: config.provider,
    ldap:   deepCopyLdap(config.ldap),
    oidc:   { ...config.oidc },
    entra:  { ...config.entra },
    google: { ...config.google },
    github: { ...config.github },
  }
}

function showSuccess(msg: string) {
  successMsg.value = msg
  setTimeout(() => (successMsg.value = null), 3000)
}

// ── Lifecycle ─────────────────────────────────────────────────────────────────

async function load() {
  loading.value = true
  loadError.value = null
  try {
    const [data, tenancy] = await Promise.all([fetchSSOConfig(), fetchTenancyConfig()])

    tenancyMode.value = tenancy.mode
    if (tenancy.mode === 'Multi') {
      tenants.value = await listTenantsForMember()
    }

    serverSnapshot = {
      provider: data.provider,
      ldap:   deepCopyLdap(data.ldap),
      oidc:   { ...data.oidc },
      entra:  { ...data.entra },
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

// ── Actions ───────────────────────────────────────────────────────────────────

function reset() {
  applySnapshot(serverSnapshot)
  saveError.value = null
  testResult.value = null
}

async function save() {
  saveError.value = null

  // Provider-specific validation
  if (config.provider === 'ldap') {
    if (!config.ldap.url.trim())    { saveError.value = 'LDAP URL is required.';    return }
    if (!config.ldap.baseDN.trim()) { saveError.value = 'Base DN is required.';     return }
  } else if (config.provider === 'oidc') {
    if (!config.oidc.issuer.trim())       { saveError.value = 'OIDC Issuer is required.';      return }
    if (!config.oidc.clientId.trim())     { saveError.value = 'OIDC Client ID is required.';   return }
    if (!config.oidc.clientSecret.trim()) { saveError.value = 'OIDC Client Secret is required.'; return }
  } else if (config.provider === 'entra') {
    if (!config.entra.tenantId.trim())     { saveError.value = 'Entra Tenant ID is required.';     return }
    if (!config.entra.clientId.trim())     { saveError.value = 'Entra Client ID is required.';     return }
    if (!config.entra.clientSecret.trim()) { saveError.value = 'Entra Client Secret is required.'; return }
  } else if (config.provider === 'google') {
    if (!config.google.clientId.trim())     { saveError.value = 'Google Client ID is required.';     return }
    if (!config.google.clientSecret.trim()) { saveError.value = 'Google Client Secret is required.'; return }
  } else if (config.provider === 'github') {
    if (!config.github.clientId.trim())     { saveError.value = 'GitHub Client ID is required.';     return }
    if (!config.github.clientSecret.trim()) { saveError.value = 'GitHub Client Secret is required.'; return }
  }

  saving.value = true
  try {
    await saveSSOConfig(takeSnapshot())
    serverSnapshot = takeSnapshot()
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
    testResult.value = await testSSOConnection()
  } catch (e: any) {
    testResult.value = { success: false, providerError: e.message || 'Unexpected error.' }
  } finally {
    testing.value = false
  }
}

onMounted(load)
</script>