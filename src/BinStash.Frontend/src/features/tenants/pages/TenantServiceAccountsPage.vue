<template>
  <div class="mb-6">
    <h1 class="text-2xl font-bold text-gray-800 dark:text-gray-100">Service Accounts</h1>
    <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">Machine identities for automated access to this tenant's repositories.</p>
  </div>

  <div v-if="loading" class="flex justify-center py-16">
    <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-violet-500"></div>
  </div>
  <div v-else-if="error" class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4 text-red-700 dark:text-red-400">
    {{ error }}
  </div>
  <div v-else-if="accounts.length === 0" class="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700/60 p-12 text-center">
    <IconRobot class="w-12 h-12 text-gray-300 dark:text-gray-600 mx-auto mb-3" />
    <p class="text-gray-500 dark:text-gray-400 font-medium mb-1">No service accounts yet</p>
    <p class="text-sm text-gray-400 dark:text-gray-500 mb-4">Create a service account to allow automated systems to access your repositories.</p>
    <button @click="showCreateModal = true" class="btn bg-violet-500 hover:bg-violet-600 text-white">Create Service Account</button>
  </div>
  <div v-else class="space-y-4">
    <div v-for="sa in accounts" :key="sa.id"
      class="bg-white dark:bg-gray-800 rounded-xl border border-gray-200 dark:border-gray-700/60 shadow-sm overflow-hidden">
      <div class="px-5 py-4 flex items-center justify-between gap-4">
        <div class="flex items-center gap-3">
          <div class="w-9 h-9 rounded-lg bg-teal-100 dark:bg-teal-900/30 flex items-center justify-center shrink-0">
            <IconRobot class="w-5 h-5 text-teal-600 dark:text-teal-400" />
          </div>
          <div>
            <div class="font-semibold text-gray-800 dark:text-gray-100">{{ sa.name }}</div>
            <div class="text-xs text-gray-500 dark:text-gray-400">Created {{ fmtDate(sa.createdAt) }}</div>
          </div>
        </div>
        <div class="flex items-center gap-2">
          <button @click="openKeys(sa)"
            class="btn border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700 text-sm flex items-center gap-2">
            <IconKey class="w-4 h-4" />
            API Keys
          </button>
          <button @click="confirmDelete(sa)"
            class="btn border border-red-200 dark:border-red-800 text-red-600 dark:text-red-400 hover:bg-red-50 dark:hover:bg-red-900/20 text-sm">
            Delete
          </button>
        </div>
      </div>
    </div>
  </div>

  <Teleport to="body">
    <!-- Create SA modal -->
    <div v-if="showCreateModal" class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div class="absolute inset-0 bg-black/40 backdrop-blur-sm" @click="showCreateModal = false" />
      <div class="relative bg-white dark:bg-gray-800 rounded-xl shadow-xl w-full max-w-md p-6 border border-gray-200 dark:border-gray-700">
        <h3 class="text-lg font-semibold text-gray-800 dark:text-gray-100 mb-4">Create Service Account</h3>
        <div>
          <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Name</label>
          <input v-model="newName" type="text" class="form-input w-full text-sm" placeholder="ci-deploy" />
        </div>
        <div v-if="createError" class="mt-3 text-sm text-red-500">{{ createError }}</div>
        <div class="mt-6 flex justify-end gap-3">
          <button @click="showCreateModal = false" class="btn border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700">Cancel</button>
          <button @click="doCreate" :disabled="creating" class="btn bg-violet-500 hover:bg-violet-600 text-white disabled:opacity-50">
            {{ creating ? 'Creating…' : 'Create' }}
          </button>
        </div>
      </div>
    </div>

    <!-- Delete confirm -->
    <div v-if="showDeleteModal && deleteTarget" class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div class="absolute inset-0 bg-black/40 backdrop-blur-sm" @click="showDeleteModal = false" />
      <div class="relative bg-white dark:bg-gray-800 rounded-xl shadow-xl w-full max-w-sm p-6 border border-gray-200 dark:border-gray-700">
        <h3 class="text-lg font-semibold text-gray-800 dark:text-gray-100 mb-2">Delete Service Account?</h3>
        <p class="text-sm text-gray-500 dark:text-gray-400">Delete <span class="font-medium text-gray-700 dark:text-gray-200">{{ deleteTarget.name }}</span>? All API keys will be revoked immediately.</p>
        <div v-if="deleteError" class="mt-3 text-sm text-red-500">{{ deleteError }}</div>
        <div class="mt-6 flex justify-end gap-3">
          <button @click="showDeleteModal = false" class="btn border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700">Cancel</button>
          <button @click="doDelete" :disabled="deleting" class="btn bg-red-500 hover:bg-red-600 text-white disabled:opacity-50">
            {{ deleting ? 'Deleting…' : 'Delete' }}
          </button>
        </div>
      </div>
    </div>

    <!-- API Keys panel -->
    <div v-if="showKeysModal && keysTarget" class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div class="absolute inset-0 bg-black/40 backdrop-blur-sm" @click="closeKeys" />
      <div class="relative bg-white dark:bg-gray-800 rounded-xl shadow-xl w-full max-w-lg p-6 border border-gray-200 dark:border-gray-700">
        <div class="flex items-center justify-between mb-4">
          <div>
            <h3 class="text-lg font-semibold text-gray-800 dark:text-gray-100">API Keys</h3>
            <p class="text-sm text-gray-500 dark:text-gray-400">{{ keysTarget.name }}</p>
          </div>
          <button @click="showNewKeyForm = true" v-if="!showNewKeyForm && !newKeyResult"
            class="btn bg-violet-500 hover:bg-violet-600 text-white text-sm flex items-center gap-2">
            <IconPlus class="w-4 h-4" /> New Key
          </button>
        </div>

        <!-- New key created banner -->
        <div v-if="newKeyResult" class="mb-4 bg-green-50 dark:bg-green-900/20 border border-green-200 dark:border-green-800 rounded-lg p-4">
          <p class="text-sm font-medium text-green-700 dark:text-green-400 mb-2">✓ API key created — copy it now, it won't be shown again.</p>
          <div class="flex items-center gap-2">
            <code class="flex-1 text-xs bg-green-100 dark:bg-green-900/30 rounded px-3 py-2 font-mono break-all text-green-800 dark:text-green-300">{{ newKeyResult.key }}</code>
            <button @click="copyKey(newKeyResult!.key)" class="btn border border-green-300 dark:border-green-700 text-green-700 dark:text-green-300 text-xs">
              {{ copied ? 'Copied!' : 'Copy' }}
            </button>
          </div>
          <button @click="newKeyResult = null" class="mt-2 text-xs text-green-600 dark:text-green-400 underline">Dismiss</button>
        </div>

        <!-- New key form -->
        <div v-if="showNewKeyForm" class="mb-4 bg-gray-50 dark:bg-gray-700/30 rounded-lg p-4 border border-gray-200 dark:border-gray-700">
          <h4 class="text-sm font-medium text-gray-700 dark:text-gray-300 mb-3">Create New API Key</h4>
          <div class="space-y-3">
            <div>
              <label class="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Display Name</label>
              <input v-model="newKeyName" type="text" class="form-input w-full text-sm" placeholder="CI Pipeline" />
            </div>
            <div>
              <label class="block text-xs font-medium text-gray-600 dark:text-gray-400 mb-1">Expires (optional)</label>
              <input v-model="newKeyExpiry" type="datetime-local" class="form-input w-full text-sm" />
            </div>
          </div>
          <div v-if="newKeyError" class="mt-2 text-xs text-red-500">{{ newKeyError }}</div>
          <div class="mt-3 flex gap-2">
            <button @click="showNewKeyForm = false; newKeyName = ''; newKeyExpiry = ''" class="btn border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-100 dark:hover:bg-gray-700 text-sm">Cancel</button>
            <button @click="doCreateKey" :disabled="creatingKey" class="btn bg-violet-500 hover:bg-violet-600 text-white text-sm disabled:opacity-50">
              {{ creatingKey ? 'Creating…' : 'Create Key' }}
            </button>
          </div>
        </div>

        <!-- Keys list -->
        <div v-if="keysLoading" class="flex justify-center py-6">
          <div class="animate-spin rounded-full h-6 w-6 border-b-2 border-violet-500"></div>
        </div>
        <div v-else-if="apiKeys.length === 0 && !showNewKeyForm" class="text-center py-6 text-gray-400 text-sm">No API keys. Create one to get started.</div>
        <div v-else class="space-y-2">
          <div v-for="k in apiKeys" :key="k.id"
            class="flex items-center justify-between gap-3 bg-gray-50 dark:bg-gray-700/30 rounded-lg px-4 py-3 border border-gray-200 dark:border-gray-700">
            <div class="min-w-0">
              <div class="font-medium text-sm text-gray-800 dark:text-gray-100">{{ k.displayName }}</div>
              <div class="text-xs text-gray-500 dark:text-gray-400 mt-0.5">
                Created {{ fmtDate(k.createdAt) }}
                <span v-if="k.expiresAt"> · Expires {{ fmtDate(k.expiresAt) }}</span>
                <span v-if="k.lastUsedAt"> · Last used {{ fmtDate(k.lastUsedAt) }}</span>
              </div>
            </div>
            <div class="flex items-center gap-2 shrink-0">
              <span :class="k.isActive ? 'bg-green-100 text-green-700 dark:bg-green-900/30 dark:text-green-400' : 'bg-gray-100 text-gray-500 dark:bg-gray-700 dark:text-gray-400'"
                class="text-xs px-2 py-0.5 rounded-full font-medium">
                {{ k.isActive ? 'Active' : 'Inactive' }}
              </span>
              <button @click="doDeleteKey(k.id)" class="text-xs text-red-500 hover:text-red-600 font-medium">Revoke</button>
            </div>
          </div>
        </div>

        <div class="mt-6 flex justify-end">
          <button @click="closeKeys" class="btn border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700">Close</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { IconPlus, IconRobot, IconKey } from '@tabler/icons-vue'
import {
  listServiceAccounts, createServiceAccount, deleteServiceAccount,
  listApiKeys, createApiKey, deleteApiKey,
  type ServiceAccountInfoDto, type ApiKeyInfoDto, type CreateApiKeyResponse,
} from '../../../api/serviceAccounts'


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
    await load()
  } catch (e: any) { createError.value = e?.message ?? 'Failed to create service account.' }
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
    showDeleteModal.value = false; await load()
  } catch (e: any) { deleteError.value = e?.message ?? 'Failed to delete.' }
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
    })
    newKeyResult.value = res
    showNewKeyForm.value = false; newKeyName.value = ''; newKeyExpiry.value = ''
    apiKeys.value = await listApiKeys(keysTarget.value.id)
  } catch (e: any) { newKeyError.value = e?.message ?? 'Failed to create API key.' }
  finally { creatingKey.value = false }
}

async function doDeleteKey(keyId: string) {
  if (!keysTarget.value) return
  try {
    await deleteApiKey(keysTarget.value.id, keyId)
    apiKeys.value = await listApiKeys(keysTarget.value.id)
  } catch {}
}

async function copyKey(key: string) {
  await navigator.clipboard.writeText(key)
  copied.value = true
  setTimeout(() => { copied.value = false }, 2000)
}

onMounted(load)
</script>