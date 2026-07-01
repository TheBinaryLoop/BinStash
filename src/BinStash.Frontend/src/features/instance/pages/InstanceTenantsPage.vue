<template>
  <!-- Page header -->
  <PageHeader title="Tenants" description="Manage all tenants on this instance.">
    <template #actions>
      <BaseButton :icon="IconPlus" @click="openCreate">New Tenant</BaseButton>
    </template>
  </PageHeader>

  <!-- Search bar -->
  <div class="mb-5 flex items-center gap-3">
    <div class="w-full max-w-sm">
      <BaseInput v-model="search" placeholder="Search tenants…" :prefix-icon="IconSearch" />
    </div>
  </div>

  <!-- Error -->
  <div
    v-if="loadError"
    class="mb-5 rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
  >
    {{ loadError }}
  </div>

  <!-- Tenant table -->
  <BaseCard v-else :padded="false">
    <DataTable :columns="columns" :items="loading ? [] : filtered" :loading="loading" row-key="tenantId">
      <template #cell-name="{ item: t }">
        <div class="flex items-center gap-3">
          <div class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent">
            <IconBuildingSkyscraper class="h-4 w-4" />
          </div>
          <div class="font-medium text-ink-strong">{{ t.name }}</div>
        </div>
      </template>

      <template #cell-slug="{ item: t }">
        <span class="font-mono text-xs text-ink-muted">{{ t.slug ?? '—' }}</span>
      </template>

      <template #cell-tenantId="{ item: t }">
        <span class="font-mono text-xs text-ink-subtle">{{ shortId(t.tenantId) }}</span>
      </template>

      <template #cell-actions="{ item: t }">
        <div class="flex items-center justify-end gap-1">
          <BaseButton variant="ghost" size="sm" :to="`/t/${t.tenantId}`">Open</BaseButton>
          <BaseButton variant="ghost" size="sm" @click="openEdit(t)">Edit</BaseButton>
          <BaseButton variant="ghost" size="sm" @click="confirmDelete(t)">Delete</BaseButton>
        </div>
      </template>

      <template #empty>
        <EmptyState
          :icon="IconBuildingSkyscraper"
          :title="search ? 'No tenants found' : 'No tenants yet'"
          :description="search ? 'No tenants match your search.' : 'Create your first tenant to get started.'"
        >
          <BaseButton v-if="!search" :icon="IconPlus" @click="openCreate">Create first tenant</BaseButton>
        </EmptyState>
      </template>
    </DataTable>
  </BaseCard>

  <!-- ── Create / Edit Modal ───────────────────────────────────────────── -->
  <BaseModal
    v-model:open="showFormModal"
    size="md"
    :title="editTarget ? 'Edit Tenant' : 'Create Tenant'"
    :description="editTarget ? 'Update tenant name and slug.' : 'Create a new tenant on this instance.'"
  >
    <form class="space-y-4" @submit.prevent="submitForm">
      <BaseInput
        v-model="formName"
        label="Name"
        placeholder="Acme Corp"
        required
        @input="autoSlug"
      />
      <BaseInput
        v-model="formSlug"
        label="Slug"
        placeholder="acme-corp"
        required
        hint="URL-safe identifier. Lowercase letters, numbers and hyphens only."
        class="font-mono"
      />
      <div v-if="formError" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
        {{ formError }}
      </div>
    </form>

    <template #footer>
      <BaseButton variant="secondary" @click="showFormModal = false">Cancel</BaseButton>
      <BaseButton :loading="formBusy" @click="submitForm">
        {{ editTarget ? 'Save' : 'Create' }}
      </BaseButton>
    </template>
  </BaseModal>

  <!-- ── Delete Confirm Modal ─────────────────────────────────────────── -->
  <BaseModal v-model:open="showDeleteModal" size="sm" title="Delete Tenant?">
    <template v-if="deleteTarget">
      <p class="text-sm text-ink-muted">
        Permanently delete
        <span class="font-semibold text-ink-strong">{{ deleteTarget.name }}</span>?
        This will remove all repositories, members and data associated with this tenant.
        <span class="font-semibold text-danger">This cannot be undone.</span>
      </p>
      <div v-if="deleteError" class="mt-3 rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
        {{ deleteError }}
      </div>
    </template>

    <template #footer>
      <BaseButton variant="secondary" @click="showDeleteModal = false">Cancel</BaseButton>
      <BaseButton variant="danger" :loading="deleteBusy" @click="doDelete">Delete</BaseButton>
    </template>
  </BaseModal>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { IconPlus, IconSearch, IconBuildingSkyscraper } from '@tabler/icons-vue'
import { PageHeader, BaseButton, BaseInput, BaseCard, BaseModal, DataTable, EmptyState } from '@/shared/components/ui'
import type { Column } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'

const toast = useToast()

const columns: Column[] = [
  { key: 'name', label: 'Name' },
  { key: 'slug', label: 'Slug' },
  { key: 'tenantId', label: 'Tenant ID' },
  { key: 'actions', label: 'Actions', align: 'right' },
]
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
    toast.success(editTarget.value ? 'Tenant updated' : 'Tenant created')
    if (shouldReload) {
      await load()
    }
  } catch (e: any) {
    formError.value = e?.message ?? 'Operation failed.'
    toast.error(formError.value)
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
    const deletedName = deleteTarget.value.name
    await adminDeleteTenant(deleteTarget.value.tenantId)
    showDeleteModal.value = false
    toast.success(`Tenant “${deletedName}” deleted`)
    await load()
  } catch (e: any) {
    deleteError.value = e?.message ?? 'Failed to delete tenant.'
    toast.error(deleteError.value)
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