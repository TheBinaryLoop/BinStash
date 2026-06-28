<template>
  <div class="space-y-6">
    <!-- Page header -->
    <div class="mb-2 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
      <div>
        <h1 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white md:text-[32px]">Users</h1>
        <p class="mt-2 text-sm text-slate-500 dark:text-slate-400">
          View instance-wide user accounts, access levels, and account status.
        </p>
      </div>
      <button
        type="button"
        class="inline-flex items-center gap-2 rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-[#7C86FF]/20 transition hover:bg-[#6d78ff] disabled:opacity-50 disabled:cursor-not-allowed"
        disabled
      >
        Invite User
      </button>
    </div>

    <!-- Stat cards -->
    <div class="grid grid-cols-12 gap-5">
      <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Total users</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-[#7C86FF]/10 text-[#7C86FF]">
            <IconUsers class="h-5 w-5" />
          </div>
        </div>
        <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">{{ userStats.total }}</div>
        <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Accounts visible at the instance scope</div>
      </div>

      <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Verified</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-emerald-500/10 text-emerald-500">
            <IconCircleCheck class="h-5 w-5" />
          </div>
        </div>
        <div class="text-[40px] font-bold leading-none text-emerald-600 dark:text-emerald-400">{{ userStats.verified }}</div>
        <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Users with confirmed email addresses</div>
      </div>

      <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Admins</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-[#7C86FF]/10 text-[#7C86FF]">
            <IconShieldCheck class="h-5 w-5" />
          </div>
        </div>
        <div class="text-[40px] font-bold leading-none text-[#7C86FF]">{{ userStats.admins }}</div>
        <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Users with instance-level administration</div>
      </div>

      <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Pending setup</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-amber-500/10 text-amber-500">
            <IconClock class="h-5 w-5" />
          </div>
        </div>
        <div class="text-[40px] font-bold leading-none text-amber-600 dark:text-amber-400">{{ userStats.pendingSetup }}</div>
        <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Accounts that have not completed onboarding</div>
      </div>
    </div>

    <!-- Search -->
    <div class="flex items-center gap-3">
      <div class="relative flex-1 max-w-sm">
        <IconSearch class="w-4 h-4 absolute left-3 top-1/2 -translate-y-1/2 text-slate-400" />
        <input
          v-model="search"
          type="text"
          placeholder="Search users…"
          class="w-full rounded-full border border-slate-200 bg-slate-50 py-2.5 pl-9 pr-4 text-sm text-slate-900 placeholder-slate-400 outline-none transition focus:border-[#7C86FF] focus:ring-2 focus:ring-[#7C86FF]/20 dark:border-white/10 dark:bg-white/[0.03] dark:text-white dark:placeholder-slate-500 dark:focus:border-[#7C86FF]"
        />
      </div>
    </div>

    <!-- User table -->
    <div class="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
      <div class="overflow-x-auto">
        <table class="w-full text-sm">
          <thead>
            <tr class="text-xs font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500 bg-slate-50 dark:bg-white/[0.03] border-b border-slate-200 dark:border-white/5">
              <th class="px-4 py-3 text-left">User</th>
              <th class="px-4 py-3 text-left">Roles</th>
              <th class="px-4 py-3 text-left">Email status</th>
              <th class="px-4 py-3 text-left">Onboarding</th>
              <th class="px-4 py-3 text-right">Actions</th>
            </tr>
          </thead>
          <tbody class="divide-y divide-slate-100 dark:divide-white/5">
            <tr v-if="loading">
              <td colspan="5" class="px-4 py-12 text-center text-slate-400 dark:text-slate-500">
                Loading users…
              </td>
            </tr>
            <tr v-else-if="errorMessage">
              <td colspan="5" class="px-4 py-12 text-center">
                <div class="space-y-3">
                  <p class="text-sm text-red-500 dark:text-red-400">{{ errorMessage }}</p>
                  <button
                    type="button"
                    class="rounded-full border border-slate-200 dark:border-white/10 px-4 py-2 text-sm font-medium text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-white/[0.03] transition"
                    @click="loadUsers"
                  >
                    Retry
                  </button>
                </div>
              </td>
            </tr>
            <tr v-if="filteredUsers.length === 0">
              <td colspan="5" class="px-4 py-12 text-center text-slate-400 dark:text-slate-500">
                No users match your search.
              </td>
            </tr>
            <tr
              v-else-if="!loading && !errorMessage"
              v-for="user in filteredUsers"
              :key="user.id"
              class="hover:bg-slate-50 dark:hover:bg-white/[0.03] transition-colors"
            >
              <td class="px-4 py-3">
                <div class="flex items-center gap-3">
                  <div class="w-9 h-9 rounded-full bg-[#7C86FF]/10 flex items-center justify-center text-[#7C86FF] font-semibold text-xs shrink-0">
                    {{ initials(user) }}
                  </div>
                  <div>
                    <div class="font-medium text-slate-900 dark:text-white flex items-center gap-2">
                      <span>{{ fullName(user) }}</span>
                      <span
                        v-if="user.isCurrentUser"
                        class="inline-flex items-center px-2 py-0.5 rounded-full text-[11px] font-medium bg-[#7C86FF]/10 text-[#7C86FF]"
                      >
                        You
                      </span>
                    </div>
                    <div class="text-xs text-slate-500 dark:text-slate-400">{{ user.email }}</div>
                  </div>
                </div>
              </td>
              <td class="px-4 py-3">
                <div class="flex flex-wrap gap-1">
                  <span
                    v-for="role in user.roles"
                    :key="role"
                    :class="roleClass(role)"
                    class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium"
                  >
                    {{ role }}
                  </span>
                  <span v-if="!user.roles.length" class="text-xs text-slate-400 dark:text-slate-500">—</span>
                </div>
              </td>
              <td class="px-4 py-3">
                <span
                  class="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium"
                  :class="user.isEmailConfirmed
                    ? 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300'
                    : 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-300'"
                >
                  {{ user.isEmailConfirmed ? 'Verified' : 'Pending verification' }}
                </span>
              </td>
              <td class="px-4 py-3">
                <span
                  class="inline-flex items-center px-2 py-1 rounded-full text-xs font-medium"
                  :class="user.onboardingCompleted
                    ? 'bg-sky-100 text-sky-700 dark:bg-sky-500/20 dark:text-sky-300'
                    : 'bg-slate-100 text-slate-700 dark:bg-white/5 dark:text-slate-300'"
                >
                  {{ user.onboardingCompleted ? 'Completed' : 'Pending setup' }}
                </span>
              </td>
              <td class="px-4 py-3 text-right">
                <div class="flex items-center justify-end gap-2">
                  <button type="button" class="text-xs font-medium text-[#7C86FF] opacity-60 cursor-not-allowed" disabled>
                    Edit roles
                  </button>
                  <button type="button" class="text-xs font-medium text-red-500 dark:text-red-400 opacity-60 cursor-not-allowed" disabled>
                    Disable
                  </button>
                </div>
              </td>
            </tr>
          </tbody>
        </table>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { IconSearch, IconUsers, IconCircleCheck, IconShieldCheck, IconClock } from '@tabler/icons-vue'
import { useAuthStore } from '@/stores/auth'
import { listInstanceUsers, type InstanceUserDto } from '@/api/users'

type InstanceUserRow = {
  id: string
  firstName: string
  middleName?: string
  lastName: string
  email: string
  isEmailConfirmed: boolean
  onboardingCompleted: boolean
  roles: string[]
  isCurrentUser?: boolean
}

const auth = useAuthStore()
const search = ref('')
const loading = ref(false)
const errorMessage = ref('')
const users = ref<InstanceUserRow[]>([])

function toInstanceUserRow(user: InstanceUserDto): InstanceUserRow {
  const currentUser = auth.user
  const isCurrentUser = currentUser
    ? currentUser.id === user.id || currentUser.email.toLowerCase() === user.email.toLowerCase()
    : false

  return {
    id: user.id,
    firstName: user.firstName,
    middleName: user.middleName ?? undefined,
    lastName: user.lastName,
    email: user.email,
    isEmailConfirmed: user.isEmailVerified,
    onboardingCompleted: user.isOnboardingCompleted,
    roles: isCurrentUser ? (currentUser?.roles ?? []) : [],
    isCurrentUser,
  }
}

async function loadUsers() {
  loading.value = true
  errorMessage.value = ''

  try {
    const data = await listInstanceUsers()
    users.value = data.map(toInstanceUserRow)
  } catch (error: any) {
    errorMessage.value = error?.message || 'Failed to load users.'
    users.value = []
  } finally {
    loading.value = false
  }
}

const filteredUsers = computed(() => {
  const q = search.value.trim().toLowerCase()
  if (!q) return users.value

  return users.value.filter(user =>
    fullName(user).toLowerCase().includes(q) ||
    user.email.toLowerCase().includes(q) ||
    user.roles.some(role => role.toLowerCase().includes(q)),
  )
})

const userStats = computed(() => {
  const items = users.value
  return {
    total: items.length,
    verified: items.filter(user => user.isEmailConfirmed).length,
    admins: items.filter(user => user.roles.includes('InstanceAdmin')).length,
    pendingSetup: items.filter(user => !user.onboardingCompleted).length,
  }
})

onMounted(() => {
  loadUsers()
})

function fullName(user: InstanceUserRow) {
  return [user.firstName, user.middleName, user.lastName].filter(Boolean).join(' ') || user.email
}

function initials(user: InstanceUserRow) {
  if (user.firstName && user.lastName) {
    return `${user.firstName[0]}${user.lastName[0]}`.toUpperCase()
  }

  return user.email.slice(0, 2).toUpperCase()
}

function roleClass(role: string) {
  if (role === 'InstanceAdmin') {
    return 'bg-[#7C86FF]/10 text-[#7C86FF]'
  }

  if (role === 'TenantAdmin') {
    return 'bg-sky-100 text-sky-700 dark:bg-sky-500/20 dark:text-sky-300'
  }

  if (role === 'TenantBillingAdmin') {
    return 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-300'
  }

  return 'bg-slate-100 text-slate-700 dark:bg-white/5 dark:text-slate-300'
}
</script>