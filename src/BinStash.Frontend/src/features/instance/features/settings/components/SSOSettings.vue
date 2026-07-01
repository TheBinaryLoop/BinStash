<template>
  <div class="space-y-6">
    <!-- Section header -->
    <div>
      <h2 class="text-lg font-semibold text-ink-strong">Single Sign-On (SSO) Configuration</h2>
      <p class="mt-0.5 text-sm text-ink-muted">
        Configure a federated authentication provider. Users will be able to sign in using the selected provider.
      </p>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center justify-center gap-3 py-8 text-ink-muted">
      <Spinner :size="20" color="var(--color-accent)" />
      <span>Loading SSO configuration…</span>
    </div>

    <!-- Load error -->
    <div
      v-else-if="loadError"
      class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
    >
      {{ loadError }}
    </div>

    <template v-else>
      <BaseCard>
        <div class="space-y-6">

          <!-- Provider selector -->
          <div>
            <label class="mb-1.5 block text-sm font-medium text-ink-strong">
              SSO Provider
            </label>
            <div class="flex flex-wrap gap-2">
              <button
                type="button"
                @click="config.provider = null"
                class="h-9 rounded-control border px-4 text-sm font-medium transition"
                :class="config.provider === null
                  ? 'border-accent bg-accent text-white'
                  : 'border-hairline bg-card text-ink-muted hover:bg-raised hover:text-ink-strong'"
              >
                None
              </button>
              <button
                v-for="p in SSO_PROVIDERS"
                :key="p.id"
                type="button"
                @click="config.provider = p.id"
                class="h-9 rounded-control border px-4 text-sm font-medium transition"
                :class="config.provider === p.id
                  ? 'border-accent bg-accent text-white'
                  : 'border-hairline bg-card text-ink-muted hover:bg-raised hover:text-ink-strong'"
              >
                {{ p.label }}
              </button>
            </div>
            <p class="mt-1.5 text-xs text-ink-subtle">
              Select <strong>None</strong> to disable SSO. Users will authenticate with local credentials only.
            </p>
          </div>

          <template v-if="config.provider !== null">
            <!-- Provider-specific config -->
            <div class="space-y-2 border-t border-hairline pt-4">
              <h3 class="text-xs font-semibold uppercase tracking-wide text-ink-subtle">
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
          <div v-else class="border-t border-hairline pt-4">
            <p class="text-sm italic text-ink-muted">
              SSO authentication is disabled.
            </p>
          </div>

          <!-- Save error -->
          <div
            v-if="saveError"
            class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
          >
            {{ saveError }}
          </div>

          <!-- Actions -->
          <div class="flex items-center gap-3 border-t border-hairline pt-4">
            <BaseButton :loading="saving" :disabled="saving" @click="save">
              {{ saving ? 'Saving…' : 'Save' }}
            </BaseButton>
            <BaseButton variant="secondary" :disabled="saving" @click="reset">
              Reset
            </BaseButton>
          </div>

          <!-- ── Test Connection ──────────────────────────────────────────── -->
          <div
            v-if="serverSnapshot.provider !== null"
            class="space-y-3 border-t border-hairline pt-4"
          >
            <div>
              <h3 class="text-sm font-medium text-ink-strong">Test Connection</h3>
              <p class="mt-0.5 text-xs text-ink-subtle">
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
              <BaseButton
                variant="secondary"
                :icon="IconPlugConnected"
                :loading="testing"
                :disabled="testing"
                @click="runTest"
              >
                {{ testing ? 'Testing…' : 'Test Connection' }}
              </BaseButton>
            </div>
            <!-- Test result -->
            <div
              v-if="testResult"
              class="flex items-start gap-2 rounded-card px-4 py-3 text-sm"
              :class="testResult.success
                ? 'border border-success/25 bg-success-soft text-success'
                : 'border border-danger/20 bg-danger-soft text-danger'"
            >
              <IconCircleCheck v-if="testResult.success" class="mt-0.5 h-4 w-4 shrink-0" />
              <IconAlertCircle v-else class="mt-0.5 h-4 w-4 shrink-0" />
              <div>
                <span v-if="testResult.success">
                  Connection test passed successfully.
                </span>
                <template v-else>
                  <span>Connection test failed.</span>
                  <div
                    v-if="testResult.providerError"
                    class="mt-1 break-all font-mono text-xs opacity-80"
                  >
                    {{ testResult.providerError }}
                  </div>
                </template>
              </div>
            </div>
          </div>

        </div>
      </BaseCard>
    </template>
  </div>
</template>

<script lang="ts" setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { IconCircleCheck, IconAlertCircle, IconPlugConnected } from '@tabler/icons-vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BaseButton, BaseCard } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'
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

const toast = useToast()

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

// ── Helpers ─────────────────────────────────────────────────────────────────────

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

// ── Lifecycle ───────────────────────────────────────────────────────────────────

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

// ── Actions ─────────────────────────────────────────────────────────────────────

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
    toast.success('SSO configuration saved.')
  } catch (e: unknown) {
    const parsed = parseApiValidationError(e, 'Failed to save SSO configuration.')
    saveError.value = parsed.generalError
    toast.error(parsed.generalError || 'Failed to save SSO configuration.')
  } finally {
    saving.value = false
  }
}

async function runTest() {
  testResult.value = null
  testing.value = true
  try {
    testResult.value = await testSSOConnection()
    if (testResult.value?.success) {
      toast.success('Connection test passed.')
    } else {
      toast.error('Connection test failed.')
    }
  } catch (e: any) {
    testResult.value = { success: false, providerError: e.message || 'Unexpected error.' }
    toast.error('Connection test failed.')
  } finally {
    testing.value = false
  }
}

onMounted(load)
</script>
