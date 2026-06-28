<template>
  <!-- Page header -->
  <div class="mb-8 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
    <div>
      <h1 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white md:text-[32px]">Tenants</h1>
      <p class="mt-2 text-sm text-slate-500 dark:text-slate-400">
        Manage all tenants on this instance.
      </p>
    </div>
    <button
      @click="openCreate"
      class="inline-flex items-center gap-2 rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-[#7C86FF]/20 transition hover:bg-[#6d78ff] hover:cursor-pointer"
    >
      <IconPlus class="w-4 h-4" />
      New Tenant
    </button>
  </div>

  <!-- Search bar -->
  <div class="mb-5 flex items-center gap-3">
    <div class="relative flex-1 max-w-sm">
      <IconSearch class="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
      <input
        v-model="search"
        type="text"
        placeholder="Search tenants…"
        class="w-full rounded-full border border-slate-200 bg-slate-50 py-2.5 pl-9 pr-4 text-sm text-slate-900 placeholder-slate-400 outline-none transition focus:border-[#7C86FF] focus:ring-2 focus:ring-[#7C86FF]/20 dark:border-white/10 dark:bg-white/[0.03] dark:text-white dark:placeholder-slate-500 dark:focus:border-[#7C86FF]"
      />
    </div>
  </div>

  <!-- Loading -->
  <div v-if="loading" class="flex justify-center py-20">
    <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-[#7C86FF]"></div>
  </div>

  <!-- Error -->
  <div
    v-else-if="loadError"
    class="rounded-[28px] border border-rose-200 bg-rose-50 p-4 text-sm text-rose-700 dark:border-rose-500/20 dark:bg-rose-500/10 dark:text-rose-400"
  >
    {{ loadError }}
  </div>

  <!-- Empty -->
  <div
    v-else-if="filtered.length === 0"
    class="rounded-[28px] border border-dashed border-slate-300 bg-white p-10 text-center shadow-sm dark:border-white/10 dark:bg-[#0F172D]"
  >
    <IconBuildingSkyscraper class="w-10 h-10 text-slate-300 dark:text-slate-600 mx-auto mb-3" />
    <p class="text-slate-500 dark:text-slate-400 font-medium">
      {{ search ? 'No tenants match your search.' : 'No tenants yet.' }}
    </p>
    <button
      v-if="!search"
      @click="openCreate"
      class="mt-4 inline-flex items-center gap-2 rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-[#7C86FF]/20 transition hover:bg-[#6d78ff] hover:cursor-pointer"
    >
      Create first tenant
    </button>
  </div>

  <!-- Tenant table -->
  <div
    v-else
    class="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm dark:border-white/5 dark:bg-[#0F172D]"
  >
    <div class="overflow-x-auto">
      <table class="w-full text-sm">
        <thead>
          <tr class="text-xs font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500 bg-slate-50 dark:bg-white/[0.03] border-b border-slate-200 dark:border-white/5">
            <th class="px-4 py-3 text-left">Name</th>
            <th class="px-4 py-3 text-left">Slug</th>
            <th class="px-4 py-3 text-left">Tenant ID</th>
            <th class="px-4 py-3 text-right">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100 dark:divide-white/5">
          <tr
            v-for="t in filtered"
            :key="t.tenantId"
            class="hover:bg-slate-50 dark:hover:bg-white/[0.03] transition-colors"
          >
            <td class="px-4 py-3">
              <div class="flex items-center gap-3">
                <div class="w-8 h-8 rounded-full bg-[#7C86FF]/10 flex items-center justify-center shrink-0">
                  <IconBuildingSkyscraper class="w-4 h-4 text-[#7C86FF]" />
                </div>
                <div class="font-medium text-slate-900 dark:text-white">{{ t.name }}</div>
              </div>
            </td>
            <td class="px-4 py-3 font-mono text-xs text-slate-500 dark:text-slate-400">
              {{ t.slug ?? '—' }}
            </td>
            <td class="px-4 py-3">
              <span class="font-mono text-xs text-slate-400 dark:text-slate-500">{{ shortId(t.tenantId) }}</span>
            </td>
            <td class="px-4 py-3 text-right">
              <div class="flex items-center justify-end gap-2">
                <router-link
                  :to="`/t/${t.tenantId}`"
                  class="text-xs text-sky-600 hover:text-sky-700 dark:text-sky-400 dark:hover:text-sky-300 font-medium"
                >
                  Open
                </router-link>
                <button
                  @click="openEdit(t)"
                  class="text-xs text-[#7C86FF] hover:text-[#6974ff] font-medium hover:cursor-pointer"
                >
                  Edit
                </button>
                <button
                  @click="confirmDelete(t)"
                  class="text-xs text-red-500 hover:text-red-600 dark:text-red-400 font-medium hover:cursor-pointer"
                >
                  Delete
                </button>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>

  <!-- ── Create / Edit Modal ───────────────────────────────────────────── -->
  <Teleport to="body">
    <div v-if="showFormModal" class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div class="absolute inset-0 bg-black/40 backdrop-blur-sm" @click="showFormModal = false" />
      <div class="relative rounded-[28px] border border-slate-200 bg-white shadow-xl w-full max-w-md p-6 dark:border-white/10 dark:bg-[#101935]">
        <h3 class="text-lg font-semibold text-slate-900 dark:text-white mb-1">
          {{ editTarget ? 'Edit Tenant' : 'Create Tenant' }}
        </h3>
        <p class="text-sm text-slate-500 dark:text-slate-400 mb-5">
          {{ editTarget ? 'Update tenant name and slug.' : 'Create a new tenant on this instance.' }}
        </p>

        <div class="space-y-4">
          <div>
            <label class="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-1">
              Name <span class="text-red-500">*</span>
            </label>
            <input
              v-model="formName"
              type="text"
              class="form-input w-full text-sm"
              placeholder="Acme Corp"
              @input="autoSlug"
            />
          </div>
          <div>
            <label class="block text-sm font-medium text-slate-700 dark:text-slate-300 mb-1">
              Slug <span class="text-red-500">*</span>
            </label>
            <input
              v-model="formSlug"
              type="text"
              class="form-input w-full text-sm font-mono"
              placeholder="acme-corp"
            />
            <p class="text-xs text-slate-400 dark:text-slate-500 mt-1">
              URL-safe identifier. Lowercase letters, numbers and hyphens only.
            </p>
          </div>
        </div>

        <div v-if="formError" class="mt-4 text-sm text-red-500">{{ formError }}</div>

        <div class="mt-6 flex justify-end gap-3">
          <button
            @click="showFormModal = false"
            class="rounded-full border border-slate-200 dark:border-white/10 px-4 py-2.5 text-sm font-medium text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-white/[0.03] hover:cursor-pointer transition"
          >
            Cancel
          </button>
          <button
            @click="submitForm"
            :disabled="formBusy"
            class="rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-[#7C86FF]/20 transition hover:bg-[#6d78ff] disabled:opacity-50 hover:cursor-pointer"
          >
            {{ formBusy ? (editTarget ? 'Saving…' : 'Creating…') : (editTarget ? 'Save' : 'Create') }}
          </button>
        </div>
      </div>
    </div>

    <!-- ── Delete Confirm Modal ─────────────────────────────────────────── -->
    <div v-if="showDeleteModal && deleteTarget" class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div class="absolute inset-0 bg-black/40 backdrop-blur-sm" @click="showDeleteModal = false" />
      <div class="relative rounded-[28px] border border-slate-200 bg-white shadow-xl w-full max-w-sm p-6 dark:border-white/10 dark:bg-[#101935]">
        <h3 class="text-lg font-semibold text-slate-900 dark:text-white mb-2">Delete Tenant?</h3>
        <p class="text-sm text-slate-500 dark:text-slate-400">
          Permanently delete
          <span class="font-semibold text-slate-700 dark:text-slate-200">{{ deleteTarget.name }}</span>?
          This will remove all repositories, members and data associated with this tenant.
          <span class="font-semibold text-red-500">This cannot be undone.</span>
        </p>
        <div v-if="deleteError" class="mt-3 text-sm text-red-500">{{ deleteError }}</div>
        <div class="mt-6 flex justify-end gap-3">
          <button
            @click="showDeleteModal = false"
            class="rounded-full border border-slate-200 dark:border-white/10 px-4 py-2.5 text-sm font-medium text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-white/[0.03] transition"
          >
            Cancel
          </button>
          <button
            @click="doDelete"
            :disabled="deleteBusy"
            class="rounded-full bg-red-500 px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-red-500/20 transition hover:bg-red-600 disabled:opacity-50"
          >
            {{ deleteBusy ? 'Deleting…' : 'Delete' }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { IconPlus, IconSearch, IconBuildingSkyscraper } from '@tabler/icons-vue'
import {
  listTenantsForMember,
  adminCreateTenant,
  adminUpdateTenant,
  adminDeleteTenant,
} from '../../../api/tenants'
import { useTenantStore } from '../../../stores/tenant'
import type { TenantSummaryDto } from '../../../stores/tenant'

// ── State ─────────────────────────────────────────────────────────────────────
const tenantStore = useTenantStore()
const loading = ref(true)
const loadError = ref<string | null>(null)
const tenants = ref<TenantSummaryDto[]>([])
const search = ref('')

const filtered = computed(() => {
  if (!search.value) return tenants.value
  const q = search.value.toLowerCase()
  return tenants.value.filter(t =>
    t.name.toLowerCase().includes(q) ||
    (t.slug ?? '').toLowerCase().includes(q) ||
    (t.tenantId ?? '').toLowerCase().includes(q),
  )
})

// ── Form modal ────────────────────────────────────────────────────────────────

const showFormModal = ref(false)
const editTarget = ref<TenantSummaryDto | null>(null)
const formName = ref('')
const formSlug = ref('')
const formError = ref<string | null>(null)
const formBusy = ref(false)

function openCreate() {
  editTarget.value = null
  formName.value = ''
  formSlug.value = ''
  formError.value = null
  showFormModal.value = true
}

function openEdit(t: TenantSummaryDto) {
  editTarget.value = t
  formName.value = t.name
  formSlug.value = t.slug ?? ''
  formError.value = null
  showFormModal.value = true
}

/** When creating, auto-generate slug from name while user hasn't manually edited it. */
let slugManuallyEdited = false
function autoSlug() {
  if (editTarget.value || slugManuallyEdited) return
  formSlug.value = formName.value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

async function submitForm() {
  formError.value = null
  if (!formName.value.trim()) { formError.value = 'Name is required.'; return }
  if (!formSlug.value.trim()) { formError.value = 'Slug is required.'; return }
  if (!/^[a-z0-9-]+$/.test(formSlug.value)) {
    formError.value = 'Slug may only contain lowercase letters, numbers, and hyphens.'
    return
  }
  formBusy.value = true
  try {
    let shouldReload = true
    if (editTarget.value) {
      await adminUpdateTenant(editTarget.value.tenantId, { name: formName.value.trim(), slug: formSlug.value.trim() })
    } else {
      const created = await adminCreateTenant({ name: formName.value.trim(), slug: formSlug.value.trim() }) as Partial<TenantSummaryDto> | null
      if (created?.tenantId) {
        const createdTenant = created as TenantSummaryDto
        tenantStore.upsertTenant(createdTenant)
        const idx = tenants.value.findIndex(t => t.tenantId === createdTenant.tenantId)
        if (idx >= 0) {
          tenants.value[idx] = createdTenant
        } else {
          tenants.value = [...tenants.value, createdTenant]
        }
        shouldReload = false
      }
    }
    showFormModal.value = false
    slugManuallyEdited = false
    if (shouldReload) {
      await load()
    }
  } catch (e: any) {
    formError.value = e?.message ?? 'Operation failed.'
  } finally {
    formBusy.value = false
  }
}

// ── Delete modal ──────────────────────────────────────────────────────────────

const showDeleteModal = ref(false)
const deleteTarget = ref<TenantSummaryDto | null>(null)
const deleteError = ref<string | null>(null)
const deleteBusy = ref(false)

function confirmDelete(t: TenantSummaryDto) {
  deleteTarget.value = t
  deleteError.value = null
  showDeleteModal.value = true
}

async function doDelete() {
  if (!deleteTarget.value) return
  deleteError.value = null
  deleteBusy.value = true
  try {
    await adminDeleteTenant(deleteTarget.value.tenantId)
    showDeleteModal.value = false
    await load()
  } catch (e: any) {
    deleteError.value = e?.message ?? 'Failed to delete tenant.'
  } finally {
    deleteBusy.value = false
  }
}

// ── Helpers ───────────────────────────────────────────────────────────────────

function shortId(id?: string) {
  if (!id) return '—'
  return id.length > 8 ? `${id.slice(0, 8)}…` : id
}

// ── Data loading ──────────────────────────────────────────────────────────────

async function load() {
  loading.value = true
  loadError.value = null
  try {
    tenants.value = await listTenantsForMember()
  } catch (e: any) {
    loadError.value = e?.message ?? 'Failed to load tenants.'
  } finally {
    loading.value = false
  }
}

onMounted(load)
</script>