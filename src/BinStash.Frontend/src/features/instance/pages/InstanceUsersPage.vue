<template>
  <div class="space-y-6">
    <!-- Page header -->
    <PageHeader
      title="Users"
      description="View instance-wide user accounts, access levels, and account status."
    >
      <template #actions>
        <BaseButton disabled>Invite User</BaseButton>
      </template>
    </PageHeader>

    <!-- Stat cards -->
    <div class="grid gap-5 sm:grid-cols-2 xl:grid-cols-4">
      <StatCard label="Total users" :value="userStats.total" :icon="IconUsers" tone="accent" hint="Accounts visible at the instance scope" />
      <StatCard label="Verified" :value="userStats.verified" :icon="IconCircleCheck" tone="success" hint="Users with confirmed email addresses" />
      <StatCard label="Admins" :value="userStats.admins" :icon="IconShieldCheck" tone="accent" hint="Users with instance-level administration" />
      <StatCard label="Pending setup" :value="userStats.pendingSetup" :icon="IconClock" tone="warning" hint="Accounts that have not completed onboarding" />
    </div>

    <!-- Search -->
    <div class="flex items-center gap-3">
      <div class="w-full max-w-sm">
        <BaseInput v-model="search" placeholder="Search users…" :prefix-icon="IconSearch" />
      </div>
    </div>

    <!-- User table -->
    <BaseCard :padded="false">
      <DataTable :columns="columns" :items="loading ? [] : filteredUsers" :loading="loading" row-key="id">
        <template #cell-user="{ item: user }">
          <div class="flex items-center gap-3">
            <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full bg-accent-soft text-xs font-semibold text-accent">
              {{ initials(user) }}
            </div>
            <div>
              <div class="flex items-center gap-2 font-medium text-ink-strong">
                <span>{{ fullName(user) }}</span>
                <BaseBadge v-if="user.isCurrentUser" tone="accent" size="sm">You</BaseBadge>
              </div>
              <div class="text-xs text-ink-muted">{{ user.email }}</div>
            </div>
          </div>
        </template>

        <template #cell-roles="{ item: user }">
          <div class="flex flex-wrap gap-1">
            <BaseBadge
              v-for="role in user.roles"
              :key="role"
              :tone="roleTone(role)"
              size="sm"
            >
              {{ role }}
            </BaseBadge>
            <span v-if="!user.roles.length" class="text-xs text-ink-subtle">—</span>
          </div>
        </template>

        <template #cell-email="{ item: user }">
          <BaseBadge :tone="user.isEmailConfirmed ? 'success' : 'warning'">
            {{ user.isEmailConfirmed ? 'Verified' : 'Pending verification' }}
          </BaseBadge>
        </template>

        <template #cell-onboarding="{ item: user }">
          <BaseBadge :tone="user.onboardingCompleted ? 'accent' : 'neutral'">
            {{ user.onboardingCompleted ? 'Completed' : 'Pending setup' }}
          </BaseBadge>
        </template>

        <template #cell-actions>
          <div class="flex items-center justify-end gap-2">
            <BaseButton variant="ghost" size="sm" disabled>Edit roles</BaseButton>
            <BaseButton variant="ghost" size="sm" disabled>Disable</BaseButton>
          </div>
        </template>

        <template #empty>
          <div v-if="errorMessage" class="px-4 py-12 text-center">
            <p class="text-sm text-danger">{{ errorMessage }}</p>
            <div class="mt-3 flex justify-center">
              <BaseButton variant="secondary" size="sm" @click="loadUsers">Retry</BaseButton>
            </div>
          </div>
          <EmptyState
            v-else
            :icon="IconUsers"
            title="No users found"
            :description="search ? 'No users match your search.' : 'No users at the instance scope yet.'"
          />
        </template>
      </DataTable>
    </BaseCard>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { IconSearch, IconUsers, IconCircleCheck, IconShieldCheck, IconClock } from '@tabler/icons-vue'
import { useAuthStore } from '@/stores/auth'
import { listInstanceUsers, type InstanceUserDto } from '@/api/users'
import { PageHeader, StatCard, BaseCard, BaseInput, BaseButton, BaseBadge, DataTable, EmptyState } from '@/shared/components/ui'
import type { Column } from '@/shared/components/ui'

const columns: Column[] = [
  { key: 'user', label: 'User' },
  { key: 'roles', label: 'Roles' },
  { key: 'email', label: 'Email status' },
  { key: 'onboarding', label: 'Onboarding' },
  { key: 'actions', label: 'Actions', align: 'right' },
]

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

function roleTone(role: string): 'accent' | 'success' | 'warning' | 'neutral' {
  if (role === 'InstanceAdmin') return 'accent'
  if (role === 'TenantAdmin') return 'success'
  if (role === 'TenantBillingAdmin') return 'warning'
  return 'neutral'
}
</script>