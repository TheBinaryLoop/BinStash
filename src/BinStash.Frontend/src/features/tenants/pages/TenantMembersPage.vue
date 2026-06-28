<template>
  <div class="mb-6">
    <h1 class="text-2xl font-bold text-gray-800 dark:text-gray-100">Members</h1>
    <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">Manage who has access to this tenant workspace.</p>
  </div>

  <!-- Search -->
  <div class="mb-4 flex items-center gap-3">
    <div class="relative flex-1 max-w-sm">
      <IconSearch class="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-gray-400" />
      <input v-model="search" type="text" placeholder="Search members…"
        class="form-input w-full pl-9 text-sm" />
    </div>
  </div>

  <!-- Loading -->
  <div v-if="loading" class="flex justify-center py-16">
    <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-violet-500"></div>
  </div>

  <!-- Error -->
  <div v-else-if="error" class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-lg p-4 text-red-700 dark:text-red-400">
    {{ error }}
  </div>

  <!-- Table -->
  <div v-else class="bg-white dark:bg-gray-800 shadow-sm rounded-xl border border-gray-200 dark:border-gray-700/60 overflow-hidden">
    <div class="overflow-x-auto">
      <table class="w-full text-sm">
        <thead>
          <tr class="text-xs font-semibold uppercase text-gray-500 dark:text-gray-400 bg-gray-50 dark:bg-gray-700/30 border-b border-gray-200 dark:border-gray-700/60">
            <th class="px-4 py-3 text-left">Member</th>
            <th class="px-4 py-3 text-left">Roles</th>
            <th class="px-4 py-3 text-left">Joined</th>
            <th v-if="isAdmin" class="px-4 py-3 text-right">Actions</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-gray-100 dark:divide-gray-700/60">
          <tr v-if="filtered.length === 0">
            <td :colspan="isAdmin ? 4 : 3" class="px-4 py-12 text-center text-gray-400">No members found.</td>
          </tr>
          <tr v-for="m in filtered" :key="m.id" class="hover:bg-gray-50 dark:hover:bg-gray-700/20 transition-colors">
            <td class="px-4 py-3">
              <div class="flex items-center gap-3">
                <div class="w-8 h-8 rounded-full bg-violet-100 dark:bg-violet-900/30 flex items-center justify-center text-violet-600 dark:text-violet-400 font-semibold text-xs shrink-0">
                  {{ initials(m) }}
                </div>
                <div>
                  <div class="font-medium text-gray-800 dark:text-gray-100">{{ fullName(m) }}</div>
                  <div class="text-xs text-gray-500 dark:text-gray-400">{{ m.email }}</div>
                </div>
              </div>
            </td>
            <td class="px-4 py-3">
              <div class="flex flex-wrap gap-1">
                <span v-for="r in m.roles" :key="r"
                  :class="roleClass(r)"
                  class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium">
                  {{ r }}
                </span>
                <span v-if="!m.roles?.length" class="text-gray-400 text-xs">—</span>
              </div>
            </td>
            <td class="px-4 py-3 text-gray-500 dark:text-gray-400 text-xs">
              {{ m.joinedAt ? fmtDate(m.joinedAt) : '—' }}
            </td>
            <td v-if="isAdmin" class="px-4 py-3 text-right">
              <div class="flex items-center justify-end gap-2">
                <button v-if="m.id !== currentUserId" @click="openEditRoles(m)"
                  class="text-xs text-violet-600 hover:text-violet-700 dark:text-violet-400 dark:hover:text-violet-300 font-medium">
                  Edit Roles
                </button>
                <button v-if="m.id !== currentUserId" @click="confirmRemove(m)"
                  class="text-xs text-red-500 hover:text-red-600 dark:text-red-400 font-medium">
                  Remove
                </button>
                <span v-if="m.id === currentUserId" class="text-xs text-gray-400">You</span>
              </div>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>

  <!-- Invite Modal -->
  <Teleport to="body">
    <div v-if="showInviteModal" class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div class="absolute inset-0 bg-black/40 backdrop-blur-sm" @click="showInviteModal = false" />
      <div class="relative bg-white dark:bg-gray-800 rounded-xl shadow-xl w-full max-w-md p-6 border border-gray-200 dark:border-gray-700">
        <h3 class="text-lg font-semibold text-gray-800 dark:text-gray-100 mb-4">Invite Member</h3>
        <div class="space-y-4">
          <div>
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Email Address</label>
            <input v-model="invite.email" type="email" class="form-input w-full text-sm" placeholder="user@example.com" />
          </div>
          <div>
            <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1">Role</label>
            <select v-model="invite.role" class="form-select w-full text-sm">
              <option value="TenantMember">TenantMember</option>
              <option value="TenantAdmin">TenantAdmin</option>
            </select>
          </div>
        </div>
        <div v-if="inviteError" class="mt-3 text-sm text-red-500">{{ inviteError }}</div>
        <div class="mt-6 flex justify-end gap-3">
          <button @click="showInviteModal = false" class="btn border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700">Cancel</button>
          <button @click="doInvite" :disabled="inviting" class="btn bg-violet-500 hover:bg-violet-600 text-white disabled:opacity-50">
            {{ inviting ? 'Sending…' : 'Send Invite' }}
          </button>
        </div>
      </div>
    </div>

    <!-- Edit Roles Modal -->
    <div v-if="showEditModal && editTarget" class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div class="absolute inset-0 bg-black/40 backdrop-blur-sm" @click="showEditModal = false" />
      <div class="relative bg-white dark:bg-gray-800 rounded-xl shadow-xl w-full max-w-md p-6 border border-gray-200 dark:border-gray-700">
        <h3 class="text-lg font-semibold text-gray-800 dark:text-gray-100 mb-1">Edit Roles</h3>
        <p class="text-sm text-gray-500 dark:text-gray-400 mb-4">{{ editTarget.email }}</p>
        <div class="space-y-2">
          <label v-for="r in availableRoles" :key="r" class="flex items-center gap-3 cursor-pointer">
            <input type="radio" :value="r" v-model="editRole" name="member-role" class="form-radio text-violet-500" />
            <span class="text-sm text-gray-700 dark:text-gray-300">{{ r }}</span>
          </label>
        </div>
        <div v-if="editError" class="mt-3 text-sm text-red-500">{{ editError }}</div>
        <div class="mt-6 flex justify-end gap-3">
          <button @click="showEditModal = false" class="btn border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700">Cancel</button>
          <button @click="doEditRoles" :disabled="editing" class="btn bg-violet-500 hover:bg-violet-600 text-white disabled:opacity-50">
            {{ editing ? 'Saving…' : 'Save' }}
          </button>
        </div>
      </div>
    </div>

    <!-- Remove Confirm Modal -->
    <div v-if="showRemoveModal && removeTarget" class="fixed inset-0 z-50 flex items-center justify-center p-4">
      <div class="absolute inset-0 bg-black/40 backdrop-blur-sm" @click="showRemoveModal = false" />
      <div class="relative bg-white dark:bg-gray-800 rounded-xl shadow-xl w-full max-w-sm p-6 border border-gray-200 dark:border-gray-700">
        <h3 class="text-lg font-semibold text-gray-800 dark:text-gray-100 mb-2">Remove Member?</h3>
        <p class="text-sm text-gray-500 dark:text-gray-400">Remove <span class="font-medium text-gray-700 dark:text-gray-200">{{ removeTarget.email }}</span> from this tenant? They will lose all access.</p>
        <div v-if="removeError" class="mt-3 text-sm text-red-500">{{ removeError }}</div>
        <div class="mt-6 flex justify-end gap-3">
          <button @click="showRemoveModal = false" class="btn border border-gray-200 dark:border-gray-700 text-gray-700 dark:text-gray-300 hover:bg-gray-50 dark:hover:bg-gray-700">Cancel</button>
          <button @click="doRemove" :disabled="removing" class="btn bg-red-500 hover:bg-red-600 text-white disabled:opacity-50">
            {{ removing ? 'Removing…' : 'Remove' }}
          </button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import { IconSearch } from '@tabler/icons-vue'
import {
  listMembers, inviteMember, updateMemberRoles, removeMember,
  type TenantMemberDto,
} from '../../../api/tenants'
import { useAuthStore } from '../../../stores/auth'
import { useTenantStore } from '../../../stores/tenant'

const auth = useAuthStore()
const currentUserId = computed(() => auth.user?.id)
const isAdmin = computed(() =>
  auth.user?.roles?.includes('InstanceAdmin') ||
  auth.user?.roles?.includes('TenantAdmin'),
)

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
function roleClass(r: string) {
  if (r === 'TenantAdmin' || r === 'InstanceAdmin') return 'bg-violet-100 text-violet-700 dark:bg-violet-900/30 dark:text-violet-400'
  return 'bg-gray-100 text-gray-600 dark:bg-gray-700 dark:text-gray-300'
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
    await load()
  } catch (e: any) {
    inviteError.value = e?.message ?? 'Failed to send invitation.'
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
    await load()
  } catch (e: any) {
    editError.value = e?.message ?? 'Failed to update roles.'
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
    await load()
  } catch (e: any) {
    removeError.value = e?.message ?? 'Failed to remove member.'
  } finally {
    removing.value = false
  }
}

onMounted(load)
</script>