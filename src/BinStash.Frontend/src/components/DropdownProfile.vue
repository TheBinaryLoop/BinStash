<template>
  <div class="relative inline-flex">
    <button
      ref="trigger"
      class="group inline-flex items-center gap-2 rounded-control p-1 pr-2 transition hover:bg-raised"
      aria-haspopup="true"
      :aria-expanded="dropdownOpen"
      @click.prevent="dropdownOpen = !dropdownOpen"
    >
      <img class="h-7 w-7 rounded-full" :src="UserAvatar" width="28" height="28" alt="User" />
      <span class="hidden max-w-32 truncate text-sm font-medium text-ink-strong sm:block">{{ currentTenantName }}</span>
      <IconChevronDown class="h-4 w-4 shrink-0 text-ink-subtle" />
    </button>

    <transition
      enter-active-class="transition ease-out duration-150 transform"
      enter-from-class="opacity-0 -translate-y-1"
      enter-to-class="opacity-100 translate-y-0"
      leave-active-class="transition ease-out duration-100"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <div
        v-show="dropdownOpen"
        class="absolute top-full z-10 mt-1.5 min-w-56 origin-top-right overflow-visible rounded-card border border-hairline bg-panel py-1.5 shadow-lg"
        :class="align === 'right' ? 'right-0' : 'left-0'"
      >
        <div class="mb-1 border-b border-hairline px-3 pb-2 pt-0.5">
          <div class="text-sm font-semibold text-ink-strong">Account</div>
          <div class="truncate text-xs text-ink-muted">{{ currentTenantLabel }}</div>
        </div>

        <ul ref="dropdown" @focusin="dropdownOpen = true" @focusout="dropdownOpen = false">
          <!-- Tenant switcher (members) -->
          <li v-if="!isInstanceAdmin" class="relative">
            <div
              class="flex cursor-default items-center justify-between gap-2 px-3 py-1.5 text-sm font-medium text-ink-muted"
              @mouseenter="openTenantFlyout"
              @mouseleave="tenantFlyoutOpen = false"
            >
              <span class="flex items-center gap-2 truncate">
                Switch tenant
                <span class="truncate text-xs text-ink-subtle">{{ currentTenantShort }}</span>
              </span>
              <IconChevronLeft class="h-4 w-4 shrink-0 text-ink-subtle" />
              <transition
                enter-active-class="transition ease-out duration-150 transform"
                enter-from-class="opacity-0 -translate-x-1"
                enter-to-class="opacity-100 translate-x-0"
                leave-active-class="transition ease-out duration-150"
                leave-from-class="opacity-100"
                leave-to-class="opacity-0"
              >
                <div
                  v-show="tenantFlyoutOpen"
                  class="absolute right-full top-0 mr-2 min-w-72 overflow-hidden rounded-card border border-hairline bg-panel py-1.5 shadow-lg"
                  @mouseenter="tenantFlyoutOpen = true"
                  @mouseleave="tenantFlyoutOpen = false"
                >
                  <div class="border-b border-hairline px-3 py-2">
                    <div class="text-xs uppercase tracking-wide text-ink-subtle">Switch tenant</div>
                  </div>
                  <div class="max-h-72 overflow-y-auto">
                    <div v-if="tenant.isLoading" class="px-3 py-2 text-sm text-ink-muted">Loading…</div>
                    <template v-else>
                      <button
                        v-for="t in tenant.tenants"
                        :key="t.tenantId"
                        type="button"
                        class="w-full px-3 py-2 text-left transition hover:bg-raised"
                        @click.stop="switchTenant(t.tenantId)"
                      >
                        <div class="flex items-start justify-between gap-3">
                          <div class="min-w-0">
                            <div class="truncate text-sm font-medium text-ink-strong">{{ t.name }}</div>
                            <div v-if="t.slug" class="truncate text-xs text-ink-muted">{{ t.slug }}</div>
                          </div>
                          <IconCheck
                            v-if="t.tenantId === tenant.currentTenantId"
                            class="h-4 w-4 shrink-0 text-accent"
                          />
                        </div>
                      </button>
                      <div v-if="tenant.tenants.length === 0" class="px-3 py-2 text-sm text-ink-muted">
                        No tenants found.
                      </div>
                    </template>
                  </div>
                </div>
              </transition>
            </div>
          </li>
          <li v-else class="px-3 py-1.5 text-sm font-medium text-ink-muted">
            Instance <span class="text-xs text-ink-subtle">Admin</span>
          </li>

          <li class="my-1 border-t border-hairline" />

          <li>
            <router-link
              class="flex items-center px-3 py-1.5 text-sm font-medium text-ink-muted transition hover:bg-raised hover:text-ink-strong"
              :to="settingsTo"
              @click="closeAll"
            >
              Settings
            </router-link>
          </li>
          <li>
            <button
              type="button"
              class="flex w-full items-center px-3 py-1.5 text-left text-sm font-medium text-danger transition hover:bg-danger-soft"
              @click="closeAll(); logout()"
            >
              Sign out
            </button>
          </li>
        </ul>
      </div>
    </transition>
  </div>
</template>

<script setup lang="ts">
import { computed, ref, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { IconChevronDown, IconChevronLeft, IconCheck } from '@tabler/icons-vue'
import UserAvatar from '../images/user-avatar-32.png'
import { useAuthStore } from '../stores/auth'
import { useTenantStore } from '../stores/tenant'
import { listTenantsForMember } from '../api/tenants'

defineProps<{ align?: 'left' | 'right' }>()

const auth = useAuthStore()
const tenant = useTenantStore()
const router = useRouter()
const route = useRoute()

const dropdownOpen = ref(false)
const tenantFlyoutOpen = ref(false)
const trigger = ref<HTMLElement | null>(null)
const dropdown = ref<HTMLElement | null>(null)

const isInstanceAdmin = computed(() => (auth.user?.roles ?? []).includes('InstanceAdmin'))

const currentTenantName = computed(() =>
  isInstanceAdmin.value ? 'Instance Admin' : (tenant.currentTenant?.name ?? 'Select tenant'),
)
const currentTenantLabel = computed(() => {
  if (isInstanceAdmin.value) return 'Instance administrator'
  const t = tenant.currentTenant
  if (!t) return 'No tenant selected'
  return t.slug ? `${t.name} (${t.slug})` : t.name
})
const currentTenantShort = computed(() => {
  if (isInstanceAdmin.value) return ''
  const t = tenant.currentTenant
  return t?.slug ? `(${t.slug})` : ''
})

const settingsTo = computed(() =>
  isInstanceAdmin.value
    ? '/instance/settings'
    : tenant.currentTenantId
      ? `/t/${tenant.currentTenantId}/settings`
      : '/select-tenant',
)

async function ensureTenantsLoaded() {
  if (tenant.isLoaded || tenant.isLoading) return
  tenant.isLoading = true
  try {
    tenant.setTenants(await listTenantsForMember())
  } finally {
    tenant.isLoading = false
  }
}

function buildTargetForTenant(newTenantId: string) {
  const full = route.fullPath
  if (full.startsWith('/t/')) return full.replace(/^\/t\/[^/]+/, `/t/${newTenantId}`)
  return `/t/${newTenantId}`
}

async function switchTenant(newTenantId: string) {
  if (!newTenantId || newTenantId === tenant.currentTenantId) return
  if (!tenant.tenants.some((t) => t.tenantId === newTenantId)) return
  tenant.setCurrentTenant(newTenantId)
  closeAll()
  await router.push(buildTargetForTenant(newTenantId))
}

async function openTenantFlyout() {
  tenantFlyoutOpen.value = true
  await ensureTenantsLoaded()
}

function closeAll() {
  tenantFlyoutOpen.value = false
  dropdownOpen.value = false
}

async function logout() {
  await auth.logout()
  await router.replace('/signin')
}

const clickHandler = ({ target }: MouseEvent) => {
  if (!dropdownOpen.value || dropdown.value?.contains(target as Node) || trigger.value?.contains(target as Node)) return
  closeAll()
}
const keyHandler = ({ keyCode }: KeyboardEvent) => {
  if (!dropdownOpen.value || keyCode !== 27) return
  closeAll()
}

onMounted(() => {
  document.addEventListener('click', clickHandler)
  document.addEventListener('keydown', keyHandler)
})
onUnmounted(() => {
  document.removeEventListener('click', clickHandler)
  document.removeEventListener('keydown', keyHandler)
})
</script>
