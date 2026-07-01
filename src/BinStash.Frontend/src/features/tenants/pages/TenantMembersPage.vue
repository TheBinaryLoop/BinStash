<template>
  <PageHeader title="Members" description="Manage who has access to this tenant workspace." />

  <div class="space-y-6">
    <!-- Search -->
    <div class="max-w-sm">
      <BaseInput v-model="search" placeholder="Search members…" :prefix-icon="IconSearch" />
    </div>

    <!-- Error -->
    <div v-if="error" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
      {{ error }}
    </div>

    <!-- Table -->
    <div v-else class="overflow-hidden rounded-card border border-hairline bg-card">
      <DataTable :columns="columns" :items="filtered" :loading="loading" row-key="id">
        <template #cell-member="{ item: m }">
          <div class="flex items-center gap-3">
            <div class="flex h-8 w-8 shrink-0 items-center justify-center rounded-full bg-accent-soft text-xs font-semibold text-accent">
              {{ initials(m) }}
            </div>
            <div class="min-w-0">
              <div class="truncate text-sm font-medium text-ink-strong">{{ fullName(m) }}</div>
              <div class="truncate text-xs text-ink-muted">{{ m.email }}</div>
            </div>
          </div>
        </template>
        <template #cell-roles="{ item: m }">
          <div class="flex flex-wrap gap-1">
            <BaseBadge
              v-for="r in m.roles"
              :key="r"
              :tone="r === 'TenantAdmin' || r === 'InstanceAdmin' ? 'accent' : 'neutral'"
            >
              {{ r }}
            </BaseBadge>
            <span v-if="!m.roles?.length" class="text-xs text-ink-subtle">—</span>
          </div>
        </template>
        <template #cell-joined="{ item: m }">
          <span class="text-xs text-ink-muted">{{ m.joinedAt ? fmtDate(m.joinedAt) : '—' }}</span>
        </template>
        <template #cell-actions="{ item: m }">
          <div class="flex items-center justify-end gap-2">
            <template v-if="m.id !== currentUserId">
              <BaseButton variant="ghost" size="sm" @click="openEditRoles(m)">Edit Roles</BaseButton>
              <BaseButton variant="ghost" size="sm" @click="confirmRemove(m)" class="text-danger hover:text-danger">Remove</BaseButton>
            </template>
            <span v-else class="text-xs text-ink-subtle">You</span>
          </div>
        </template>
        <template #empty>
          <EmptyState :icon="IconUsers" title="No members found" description="Try adjusting your search." />
        </template>
      </DataTable>
    </div>
  </div>

  <!-- Invite Modal -->
  <BaseModal v-model:open="showInviteModal" title="Invite Member" size="sm">
    <div class="space-y-4">
      <BaseInput v-model="invite.email" type="email" label="Email Address" placeholder="user@example.com" />
      <BaseSelect
        v-model="invite.role"
        label="Role"
        :options="[
          { value: 'TenantMember', label: 'TenantMember' },
          { value: 'TenantAdmin', label: 'TenantAdmin' },
        ]"
      />
      <div v-if="inviteError" class="text-sm text-danger">{{ inviteError }}</div>
    </div>
    <template #footer>
      <BaseButton variant="secondary" @click="showInviteModal = false">Cancel</BaseButton>
      <BaseButton :loading="inviting" @click="doInvite">{{ inviting ? 'Sending…' : 'Send Invite' }}</BaseButton>
    </template>
  </BaseModal>

  <!-- Edit Roles Modal -->
  <BaseModal v-model:open="showEditModal" title="Edit Roles" :description="editTarget?.email" size="sm">
    <div class="space-y-4">
      <BaseRadioGroup v-model="editRole" :options="availableRoles.map(r => ({ value: r, label: r }))" />
      <div v-if="editError" class="text-sm text-danger">{{ editError }}</div>
    </div>
    <template #footer>
      <BaseButton variant="secondary" @click="showEditModal = false">Cancel</BaseButton>
      <BaseButton :loading="editing" @click="doEditRoles">{{ editing ? 'Saving…' : 'Save' }}</BaseButton>
    </template>
  </BaseModal>

  <!-- Remove Confirm Modal -->
  <BaseModal v-model:open="showRemoveModal" title="Remove Member?" size="sm">
    <div class="space-y-3">
      <p class="text-sm text-ink-muted">
        Remove <span class="font-medium text-ink-strong">{{ removeTarget?.email }}</span> from this tenant? They will lose all access.
      </p>
      <div v-if="removeError" class="text-sm text-danger">{{ removeError }}</div>
    </div>
    <template #footer>
      <BaseButton variant="secondary" @click="showRemoveModal = false">Cancel</BaseButton>
      <BaseButton variant="danger" :loading="removing" @click="doRemove">{{ removing ? 'Removing…' : 'Remove' }}</BaseButton>
    </template>
  </BaseModal>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { IconSearch, IconUsers } from '@tabler/icons-vue'
import {
  listMembers, inviteMember, updateMemberRoles, removeMember,
  type TenantMemberDto,
} from '../../../api/tenants'
import { useAuthStore } from '../../../stores/auth'
import { useTenantStore } from '../../../stores/tenant'
import {
  PageHeader, BaseButton, BaseInput, BaseSelect, BaseRadioGroup,
  BaseModal, BaseBadge, DataTable, EmptyState, type Column,
} from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'

const toast = useToast()

const auth = useAuthStore()
const currentUserId = computed(() => auth.user?.id)
const isAdmin = computed(() =>
  auth.user?.roles?.includes('InstanceAdmin') ||
  auth.user?.roles?.includes('TenantAdmin'),
)

const columns = computed<Column[]>(() => [
  { key: 'member', label: 'Member' },
  { key: 'roles', label: 'Roles' },
  { key: 'joined', label: 'Joined' },
  ...(isAdmin.value ? [{ key: 'actions', label: 'Actions', align: 'right' as const }] : []),
])

const loading = ref(true)
const error = ref<string | null>(null)
const members = ref<TenantMemberDto[]>([])
const search = ref('')
const filtered = computed(() =>
  members.value.filter(m =>
    !search.value ||
    fullName(m).toLowerCase().includes(search.value.toLowerCase()) ||
    m.email.toLowerCase().includes(search.value.toLowerCase()),
  ),
)

// Invite
const showInviteModal = ref(false)
const invite = ref({ email: '', role: 'TenantMember' })
const inviting = ref(false)
const inviteError = ref<string | null>(null)

// Edit roles
const showEditModal = ref(false)
const editTarget = ref<TenantMemberDto | null>(null)
const editRole = ref<string>('TenantMember')
const editing = ref(false)
const editError = ref<string | null>(null)
const availableRoles = ['TenantAdmin', 'TenantBillingAdmin', 'TenantMember']

// Remove
const showRemoveModal = ref(false)
const removeTarget = ref<TenantMemberDto | null>(null)
const removing = ref(false)
const removeError = ref<string | null>(null)

function fullName(m: TenantMemberDto) {
  const parts = [m.firstName, m.lastName].filter(Boolean)
  return parts.length ? parts.join(' ') : m.email
}
function initials(m: TenantMemberDto) {
  if (m.firstName && m.lastName) return (m.firstName[0] + m.lastName[0]).toUpperCase()
  return m.email.slice(0, 2).toUpperCase()
}
function fmtDate(s: string) {
  return new Date(s).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}
async function load() {
  loading.value = true
  error.value = null
  try {
    members.value = await listMembers()
  } catch (e: any) {
    error.value = e?.message ?? 'Failed to load members.'
  } finally {
    loading.value = false
  }
}

async function doInvite() {
  inviteError.value = null
  if (!invite.value.email.trim()) { inviteError.value = 'Email is required.'; return }
  inviting.value = true
  try {
    await inviteMember({ email: invite.value.email.trim(), roles: [invite.value.role] })
    showInviteModal.value = false
    invite.value = { email: '', role: 'TenantMember' }
    toast.success('Invitation sent')
    await load()
  } catch (e: any) {
    inviteError.value = e?.message ?? 'Failed to send invitation.'
    toast.error(inviteError.value)
  } finally {
    inviting.value = false
  }
}

function openEditRoles(m: TenantMemberDto) {
  editTarget.value = m
  const existingRole = availableRoles.find(role => (m.roles ?? []).includes(role))
  editRole.value = existingRole ?? 'TenantMember'
  editError.value = null
  showEditModal.value = true
}

async function doEditRoles() {
  if (!editTarget.value) return
  editError.value = null
  if (!editRole.value) { editError.value = 'Please select a role.'; return }
  editing.value = true
  try {
    await updateMemberRoles(editTarget.value.id, { roles: [editRole.value] })
    showEditModal.value = false
    toast.success('Roles updated')
    await load()
  } catch (e: any) {
    editError.value = e?.message ?? 'Failed to update roles.'
    toast.error(editError.value)
  } finally {
    editing.value = false
  }
}

function confirmRemove(m: TenantMemberDto) {
  removeTarget.value = m
  removeError.value = null
  showRemoveModal.value = true
}

async function doRemove() {
  if (!removeTarget.value) return
  removeError.value = null
  removing.value = true
  try {
    await removeMember(removeTarget.value.id)
    showRemoveModal.value = false
    toast.success('Member removed')
    await load()
  } catch (e: any) {
    removeError.value = e?.message ?? 'Failed to remove member.'
    toast.error(removeError.value)
  } finally {
    removing.value = false
  }
}

onMounted(load)
</script>