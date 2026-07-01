<template>
  <div class="space-y-6">
    <div>
      <h2 class="text-lg font-semibold text-ink-strong">Single Sign-On (SSO) Configuration</h2>
      <p class="mt-0.5 text-sm text-ink-muted">
        Configure tenant-level federated authentication providers.
      </p>
    </div>

    <div class="rounded-card border border-warning/25 bg-warning-soft p-3 text-xs text-warning">
      LDAP is temporarily disabled for tenant settings.
    </div>

    <div v-if="loading" class="flex items-center justify-center gap-3 py-8 text-ink-muted">
      <Spinner :size="20" color="var(--color-accent)" />
      <span>Loading SSO configuration…</span>
    </div>

    <div
      v-else-if="loadError"
      class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
    >
      {{ loadError }}
    </div>

    <template v-else>
      <BaseCard class="space-y-6">

        <div>
          <p class="mb-1.5 block text-sm font-medium text-ink-strong">SSO Provider</p>
          <div class="flex flex-wrap gap-2">
            <BaseButton
              :variant="config.provider === null ? 'primary' : 'secondary'"
              size="sm"
              @click="config.provider = null"
            >None</BaseButton>
            <BaseButton
              v-for="p in SSO_PROVIDERS"
              :key="p.id"
              :variant="config.provider === p.id ? 'primary' : 'secondary'"
              size="sm"
              @click="config.provider = p.id"
            >{{ p.label }}</BaseButton>
          </div>
          <p class="mt-1.5 text-xs text-ink-subtle">
            Select <strong>None</strong> to disable SSO. Users will authenticate with local credentials only.
          </p>
        </div>

        <template v-if="config.provider !== null">
          <div class="space-y-2 border-t border-hairline pt-4">
            <h3 class="text-xs font-semibold uppercase tracking-wide text-ink-subtle">
              {{ activeProviderLabel }} Settings
            </h3>
            <OIDCConfigForm v-if="config.provider === 'oidc'" v-model="config.oidc" />
            <EntraIDConfigForm v-else-if="config.provider === 'entra'" v-model="config.entra" />
            <GoogleConfigForm v-else-if="config.provider === 'google'" v-model="config.google" />
            <GitHubConfigForm v-else-if="config.provider === 'github'" v-model="config.github" />
          </div>
        </template>

        <div v-else class="border-t border-hairline pt-4">
          <p class="text-sm italic text-ink-muted">
            SSO authentication is disabled.
          </p>
        </div>

        <div
          v-if="saveError"
          class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
        >
          {{ saveError }}
        </div>

        <div class="flex items-center gap-3 border-t border-hairline pt-4">
          <BaseButton :loading="saving" @click="save">{{ saving ? 'Saving…' : 'Save' }}</BaseButton>
          <BaseButton variant="secondary" :disabled="saving" @click="reset">Reset</BaseButton>
        </div>

        <div
          v-if="serverSnapshot.provider !== null"
          class="space-y-3 border-t border-hairline pt-4"
        >
          <div>
            <h3 class="text-sm font-medium text-ink-strong">Test Connection</h3>
            <p class="mt-0.5 text-xs text-ink-subtle">
              Verify the saved {{ activeServerProviderLabel }} configuration by fetching the provider metadata endpoint.
            </p>
          </div>
          <div>
            <BaseButton
              variant="secondary"
              :icon="testing ? undefined : IconPlugConnected"
              :loading="testing"
              @click="runTest"
            >{{ testing ? 'Testing…' : 'Test Connection' }}</BaseButton>
          </div>
          <div
            v-if="testResult"
            class="flex items-start gap-2 rounded-card border px-4 py-3 text-sm"
            :class="testResult.success
              ? 'border-success/25 bg-success-soft text-success'
              : 'border-danger/20 bg-danger-soft text-danger'"
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

      </BaseCard>
    </template>
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
import { BaseButton, BaseCard } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'

const toast = useToast()
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
  toast.success(msg)
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
    if (saveError.value) toast.error(saveError.value)
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
