<template>
  <PageHeader title="Service Accounts" description="Machine identities for automated access to this tenant's repositories.">
    <template #actions>
      <BaseButton v-if="!loading && !error && accounts.length" :icon="IconPlus" @click="showCreateModal = true">
        Create Service Account
      </BaseButton>
    </template>
  </PageHeader>

  <div v-if="loading" class="flex justify-center py-16">
    <Spinner :size="28" color="var(--color-accent)" />
  </div>
  <div v-else-if="error" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
    {{ error }}
  </div>
  <BaseCard v-else-if="accounts.length === 0">
    <EmptyState
      :icon="IconRobot"
      title="No service accounts yet"
      description="Create a service account to allow automated systems to access your repositories."
    >
      <BaseButton :icon="IconPlus" @click="showCreateModal = true">Create Service Account</BaseButton>
    </EmptyState>
  </BaseCard>
  <div v-else class="grid gap-4 sm:grid-cols-2 xl:grid-cols-3">
    <BaseCard v-for="sa in accounts" :key="sa.id" :padded="false">
      <div class="flex items-start justify-between gap-4 p-5">
        <div class="flex min-w-0 items-center gap-3">
          <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-accent-soft text-accent">
            <IconRobot class="h-5 w-5" />
          </div>
          <div class="min-w-0">
            <div class="truncate font-semibold text-ink-strong">{{ sa.name }}</div>
            <div class="text-xs text-ink-muted">Created {{ fmtDate(sa.createdAt) }}</div>
          </div>
        </div>
      </div>
      <div class="flex items-center gap-2 border-t border-hairline px-5 py-3">
        <BaseButton variant="secondary" size="sm" :icon="IconKey" @click="openKeys(sa)">API Keys</BaseButton>
        <BaseButton variant="ghost" size="sm" class="text-danger hover:text-danger" @click="confirmDelete(sa)">Delete</BaseButton>
      </div>
    </BaseCard>
  </div>

  <!-- Create SA modal -->
  <BaseModal v-model:open="showCreateModal" title="Create Service Account" size="sm">
    <div class="space-y-4">
      <BaseInput v-model="newName" label="Name" placeholder="ci-deploy" />
      <div v-if="createError" class="text-sm text-danger">{{ createError }}</div>
    </div>
    <template #footer>
      <BaseButton variant="secondary" @click="showCreateModal = false">Cancel</BaseButton>
      <BaseButton :loading="creating" @click="doCreate">{{ creating ? 'Creating…' : 'Create' }}</BaseButton>
    </template>
  </BaseModal>

  <!-- Delete confirm -->
  <BaseModal v-model:open="showDeleteModal" title="Delete Service Account?" size="sm">
    <div class="space-y-3">
      <p class="text-sm text-ink-muted">
        Delete <span class="font-medium text-ink-strong">{{ deleteTarget?.name }}</span>? All API keys will be revoked immediately.
      </p>
      <div v-if="deleteError" class="text-sm text-danger">{{ deleteError }}</div>
    </div>
    <template #footer>
      <BaseButton variant="secondary" @click="showDeleteModal = false">Cancel</BaseButton>
      <BaseButton variant="danger" :loading="deleting" @click="doDelete">{{ deleting ? 'Deleting…' : 'Delete' }}</BaseButton>
    </template>
  </BaseModal>

  <!-- API Keys panel -->
  <BaseModal v-model:open="showKeysModal" size="lg" @close="closeKeys">
    <template #header>
      <div class="flex items-start justify-between gap-4">
        <div class="min-w-0">
          <h2 class="text-base font-semibold text-ink-strong">API Keys</h2>
          <p class="mt-1 text-sm text-ink-muted">{{ keysTarget?.name }}</p>
        </div>
        <BaseButton
          v-if="!showNewKeyForm && !newKeyResult"
          size="sm"
          :icon="IconPlus"
          @click="showNewKeyForm = true"
        >New Key</BaseButton>
      </div>
    </template>

    <!-- New key created banner -->
    <div v-if="newKeyResult" class="mb-4 rounded-card border border-success/25 bg-success-soft p-4">
      <p class="mb-2 text-sm font-medium text-success">API key created — copy it now, it won't be shown again.</p>
      <div class="flex items-center gap-2">
        <code class="flex-1 break-all rounded-control bg-raised px-3 py-2 font-mono text-xs text-ink-strong">{{ newKeyResult.key }}</code>
        <BaseButton variant="secondary" size="sm" @click="copyKey(newKeyResult!.key)">
          {{ copied ? 'Copied!' : 'Copy' }}
        </BaseButton>
      </div>
      <button type="button" @click="newKeyResult = null" class="mt-2 text-xs font-medium text-success underline">Dismiss</button>
    </div>

    <!-- New key form -->
    <div v-if="showNewKeyForm" class="mb-4 rounded-card border border-hairline bg-raised p-4">
      <h4 class="mb-3 text-sm font-medium text-ink-strong">Create New API Key</h4>
      <div class="space-y-3">
        <BaseInput v-model="newKeyName" label="Display Name" placeholder="CI Pipeline" />
        <BaseInput v-model="newKeyExpiry" type="datetime-local" label="Expires (optional)" />
        <div>
          <p class="mb-1.5 block text-sm font-medium text-ink-strong">Scopes</p>
          <div class="space-y-2">
            <BaseCheckbox v-for="s in SCOPE_OPTIONS" :key="s.value" v-model="newKeyScopes" :value="s.value">
              <span class="leading-snug">
                <span class="font-medium text-ink-strong">{{ s.label }}</span>
                <code class="ml-1 text-[10px] text-ink-subtle">{{ s.value }}</code>
                <span class="block text-xs text-ink-muted">{{ s.desc }}</span>
              </span>
            </BaseCheckbox>
          </div>
        </div>
      </div>
      <div v-if="newKeyError" class="mt-2 text-xs text-danger">{{ newKeyError }}</div>
      <div class="mt-3 flex gap-2">
        <BaseButton
          variant="secondary"
          size="sm"
          @click="showNewKeyForm = false; newKeyName = ''; newKeyExpiry = ''; newKeyScopes = ['tenant:member']"
        >Cancel</BaseButton>
        <BaseButton size="sm" :loading="creatingKey" @click="doCreateKey">
          {{ creatingKey ? 'Creating…' : 'Create Key' }}
        </BaseButton>
      </div>
    </div>

    <!-- Keys list -->
    <div v-if="keysLoading" class="flex justify-center py-6">
      <Spinner :size="24" color="var(--color-accent)" />
    </div>
    <div v-else-if="apiKeys.length === 0 && !showNewKeyForm" class="py-6 text-center text-sm text-ink-subtle">
      No API keys. Create one to get started.
    </div>
    <div v-else class="space-y-2">
      <div
        v-for="k in apiKeys"
        :key="k.id"
        class="flex items-center justify-between gap-3 rounded-card border border-hairline bg-raised px-4 py-3"
      >
        <div class="min-w-0">
          <div class="text-sm font-medium text-ink-strong">{{ k.displayName }}</div>
          <div class="mt-0.5 text-xs text-ink-muted">
            Created {{ fmtDate(k.createdAt) }}
            <span v-if="k.expiresAt"> · Expires {{ fmtDate(k.expiresAt) }}</span>
            <span v-if="k.lastUsedAt"> · Last used {{ fmtDate(k.lastUsedAt) }}</span>
          </div>
          <div v-if="k.scopes && k.scopes.length" class="mt-1 flex flex-wrap gap-1">
            <BaseBadge v-for="s in k.scopes" :key="s" tone="accent">{{ s }}</BaseBadge>
          </div>
        </div>
        <div class="flex shrink-0 items-center gap-2">
          <BaseBadge :tone="k.isActive ? 'success' : 'neutral'">{{ k.isActive ? 'Active' : 'Inactive' }}</BaseBadge>
          <BaseButton variant="ghost" size="sm" class="text-danger hover:text-danger" @click="doDeleteKey(k.id)">Revoke</BaseButton>
        </div>
      </div>
    </div>

    <template #footer>
      <BaseButton variant="secondary" @click="closeKeys">Close</BaseButton>
    </template>
  </BaseModal>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { IconPlus, IconRobot, IconKey } from '@tabler/icons-vue'
import {
  listServiceAccounts, createServiceAccount, deleteServiceAccount,
  listApiKeys, createApiKey, deleteApiKey,
  type ServiceAccountInfoDto, type ApiKeyInfoDto, type CreateApiKeyResponse,
} from '../../../api/serviceAccounts'
import {
  PageHeader, BaseButton, BaseInput, BaseCheckbox, BaseBadge,
  BaseCard, BaseModal, EmptyState,
} from '@/shared/components/ui'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { useToast } from '@/composables/useToast'

const toast = useToast()

const loading = ref(true)
const error = ref<string | null>(null)
const accounts = ref<ServiceAccountInfoDto[]>([])

// Create
const showCreateModal = ref(false)
const newName = ref('')
const creating = ref(false)
const createError = ref<string | null>(null)

// Delete
const showDeleteModal = ref(false)
const deleteTarget = ref<ServiceAccountInfoDto | null>(null)
const deleting = ref(false)
const deleteError = ref<string | null>(null)

// API Keys panel
const showKeysModal = ref(false)
const keysTarget = ref<ServiceAccountInfoDto | null>(null)
const keysLoading = ref(false)
const apiKeys = ref<ApiKeyInfoDto[]>([])
const showNewKeyForm = ref(false)
const newKeyName = ref('')
const newKeyExpiry = ref('')
const SCOPE_OPTIONS = [
  { value: 'tenant:member', label: 'Member', desc: 'Publish releases, run ingest, read repositories.' },
  { value: 'tenant:admin', label: 'Admin', desc: 'Administer the tenant and create repositories.' },
  { value: 'tenant:billing', label: 'Billing', desc: 'Manage billing for the tenant.' },
]
const newKeyScopes = ref<string[]>(['tenant:member'])
const creatingKey = ref(false)
const newKeyError = ref<string | null>(null)
const newKeyResult = ref<CreateApiKeyResponse | null>(null)
const copied = ref(false)

function fmtDate(s: string) {
  return new Date(s).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

async function load() {
  loading.value = true; error.value = null
  try { accounts.value = await listServiceAccounts() }
  catch (e: any) { error.value = e?.message ?? 'Failed to load service accounts.' }
  finally { loading.value = false }
}

async function doCreate() {
  if (!newName.value.trim()) { createError.value = 'Name is required.'; return }
  creating.value = true; createError.value = null
  try {
    await createServiceAccount({ name: newName.value.trim() })
    showCreateModal.value = false; newName.value = ''
    toast.success('Service account created')
    await load()
  } catch (e: any) { createError.value = e?.message ?? 'Failed to create service account.'; toast.error(createError.value) }
  finally { creating.value = false }
}

function confirmDelete(sa: ServiceAccountInfoDto) {
  deleteTarget.value = sa; deleteError.value = null; showDeleteModal.value = true
}
async function doDelete() {
  if (!deleteTarget.value) return
  deleting.value = true; deleteError.value = null
  try {
    await deleteServiceAccount(deleteTarget.value.id)
    showDeleteModal.value = false
    toast.success('Service account deleted')
    await load()
  } catch (e: any) { deleteError.value = e?.message ?? 'Failed to delete.'; toast.error(deleteError.value) }
  finally { deleting.value = false }
}

async function openKeys(sa: ServiceAccountInfoDto) {
  keysTarget.value = sa; showKeysModal.value = true
  showNewKeyForm.value = false; newKeyResult.value = null; newKeyError.value = null
  keysLoading.value = true
  try { apiKeys.value = await listApiKeys(sa.id) }
  catch { apiKeys.value = [] }
  finally { keysLoading.value = false }
}
function closeKeys() {
  showKeysModal.value = false; keysTarget.value = null; apiKeys.value = []
  showNewKeyForm.value = false; newKeyResult.value = null
}

async function doCreateKey() {
  if (!keysTarget.value) return
  if (!newKeyName.value.trim()) { newKeyError.value = 'Display name is required.'; return }
  creatingKey.value = true; newKeyError.value = null
  try {
    const res = await createApiKey(keysTarget.value.id, {
      displayName: newKeyName.value.trim(),
      expiresAt: newKeyExpiry.value ? new Date(newKeyExpiry.value).toISOString() : null,
      scopes: [...newKeyScopes.value],
    })
    newKeyResult.value = res
    showNewKeyForm.value = false; newKeyName.value = ''; newKeyExpiry.value = ''; newKeyScopes.value = ['tenant:member']
    toast.success('API key created')
    apiKeys.value = await listApiKeys(keysTarget.value.id)
  } catch (e: any) { newKeyError.value = e?.message ?? 'Failed to create API key.'; toast.error(newKeyError.value) }
  finally { creatingKey.value = false }
}

async function doDeleteKey(keyId: string) {
  if (!keysTarget.value) return
  try {
    await deleteApiKey(keysTarget.value.id, keyId)
    toast.success('API key revoked')
    apiKeys.value = await listApiKeys(keysTarget.value.id)
  } catch (e: any) { toast.error(e?.message ?? 'Failed to revoke API key.') }
}

async function copyKey(key: string) {
  await navigator.clipboard.writeText(key)
  copied.value = true
  toast.success('API key copied')
  setTimeout(() => { copied.value = false }, 2000)
}

onMounted(load)
</script>