<template>
  <div class="relative inline-flex">
    <button
      ref="trigger"
      class="inline-flex justify-center items-center group"
      aria-haspopup="true"
      @click.prevent="dropdownOpen = !dropdownOpen"
      :aria-expanded="dropdownOpen"
    >
      <img class="w-8 h-8 rounded-full" :src="UserAvatar" width="32" height="32" alt="User" />
      <div class="flex items-center truncate">
        <span
          class="truncate ml-2 text-sm font-medium text-gray-600 dark:text-gray-100 group-hover:text-gray-800 dark:group-hover:text-white"
        >
          {{ currentTenantName }}
        </span>
        <svg class="w-3 h-3 shrink-0 ml-1 fill-current text-gray-400 dark:text-gray-500" viewBox="0 0 12 12">
          <path d="M5.9 11.4L.5 6l1.4-1.4 4 4 4-4L11.3 6z" />
        </svg>
      </div>
    </button>

    <transition
      enter-active-class="transition ease-out duration-200 transform"
      enter-from-class="opacity-0 -translate-y-2"
      enter-to-class="opacity-100 translate-y-0"
      leave-active-class="transition ease-out duration-200"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <div
        v-show="dropdownOpen"
        class="origin-top-right z-10 absolute top-full min-w-44 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700/60 py-1.5 rounded-lg shadow-lg overflow-visible mt-1"
        :class="align === 'right' ? 'right-0' : 'left-0'"
      >
        <div class="pt-0.5 pb-2 px-3 mb-1 border-b border-gray-200 dark:border-gray-700/60">
          <div class="font-medium text-gray-800 dark:text-gray-100">Account</div>
          <div class="text-xs text-gray-500 dark:text-gray-400 italic truncate">
            {{ currentTenantLabel }}
          </div>
        </div>

        <ul ref="dropdown" @focusin="dropdownOpen = true" @focusout="dropdownOpen = false">
          <!-- Conditional flyout entry -->
          <li v-if="!isInstanceAdmin" class="relative">
            <!-- Tenant flyout (existing logic) -->
            <div
              class="font-medium text-sm text-violet-500 hover:text-violet-600 dark:hover:text-violet-400 flex items-center justify-between py-1 px-3 cursor-default"
              @mouseenter="openTenantFlyout"
              @mouseleave="tenantFlyoutOpen = false"
            >
              <span class="flex items-center gap-2 truncate">
                Tenant
                <span class="text-xs text-gray-400 dark:text-gray-500 truncate">
                  {{ currentTenantShort }}
                </span>
              </span>
              <svg class="w-3 h-3 shrink-0 ml-2 fill-current text-gray-400 dark:text-gray-500" viewBox="0 0 12 12">
                <path d="M7.7 1.7 6.3.3.6 6l5.7 5.7 1.4-1.4L3.4 6z" />
              </svg>
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
                  class="absolute top-0 right-full mr-2 min-w-72 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700/60 py-1.5 rounded-lg shadow-lg overflow-hidden"
                  @mouseenter="tenantFlyoutOpen = true"
                  @mouseleave="tenantFlyoutOpen = false"
                >
                  <div class="px-3 py-2 border-b border-gray-200 dark:border-gray-700/60">
                    <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Switch tenant</div>
                  </div>
                  <div class="max-h-72 overflow-y-auto">
                    <div v-if="tenant.isLoading" class="px-3 py-2 text-sm text-gray-600 dark:text-gray-300">
                      Loading…
                    </div>
                    <template v-else>
                      <button
                        v-for="t in tenant.tenants"
                        :key="t.tenantId"
                        type="button"
                        class="w-full text-left px-3 py-2 hover:bg-gray-50 dark:hover:bg-gray-900/20"
                        @click.stop="switchTenant(t.tenantId)"
                      >
                        <div class="flex items-start justify-between gap-3">
                          <div class="min-w-0">
                            <div class="font-medium text-gray-800 dark:text-gray-100 truncate">
                              {{ t.name }}
                            </div>
                            <div v-if="t.slug" class="text-xs text-gray-500 dark:text-gray-400 truncate">
                              {{ t.slug }}
                            </div>
                          </div>
                          <svg
                            v-if="t.tenantId === tenant.currentTenantId"
                            class="fill-current text-violet-500 shrink-0"
                            width="16"
                            height="16"
                            viewBox="0 0 16 16"
                          >
                            <path d="M6.5 12.3 2.8 8.6l1.4-1.4 2.3 2.3 5.2-5.2 1.4 1.4z" />
                          </svg>
                        </div>
                      </button>
                      <div
                        v-if="tenant.tenants.length === 0"
                        class="px-3 py-2 text-sm text-gray-600 dark:text-gray-300"
                      >
                        No tenants found.
                      </div>
                    </template>
                  </div>
                </div>
              </transition>
            </div>
          </li>
          <li v-else class="relative">
            <!-- Instance flyout for InstanceAdmins -->
            <div
              class="font-medium text-sm text-violet-500 hover:text-violet-600 dark:hover:text-violet-400 flex items-center justify-between py-1 px-3 cursor-default"
            >
              <span class="flex items-center gap-2 truncate">
                Instance
                <span class="text-xs text-gray-400 dark:text-gray-500 truncate">
                  Admin
                </span>
              </span>
            </div>
          </li>

          <li class="my-1 border-t border-gray-200 dark:border-gray-700/60"></li>

          <li>
            <router-link
              class="font-medium text-sm text-violet-500 hover:text-violet-600 dark:hover:text-violet-400 flex items-center py-1 px-3"
              to="/settings/account"
              @click="closeAll"
            >
              Settings
            </router-link>
          </li>
          <li>
            <p
              class="font-medium text-sm text-violet-500 hover:text-violet-600 dark:hover:text-violet-400 hover:cursor-pointer flex items-center py-1 px-3"
              to="/signin"
              @click="closeAll(); logout()"
            >
              Sign Out
          </p>
          </li>
        </ul>
      </div>
    </transition>
  </div>
</template>

<script>
import { computed, ref, onMounted, onUnmounted } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import UserAvatar from '../images/user-avatar-32.png'

import { useAuthStore } from '../stores/auth'
import { useTenantStore } from '../stores/tenant'
import { listTenantsForMember } from '../api/tenants'

export default {
  name: 'DropdownProfile',
  props: ['align'],
  data() {
    return {
      UserAvatar: UserAvatar,
    }
  },
  setup() {
    const auth = useAuthStore()
    const tenant = useTenantStore()
    const router = useRouter()
    const route = useRoute()
    const isInstanceAdmin = auth.isInstanceAdmin

    const dropdownOpen = ref(false)
    const tenantFlyoutOpen = ref(false)

    const trigger = ref(null)
    const dropdown = ref(null)

    const currentTenantName = computed(() => {
      if (isInstanceAdmin) return 'Instance Admin'
      return tenant.currentTenant?.name ?? 'Select tenant'
    })
    const currentTenantLabel = computed(() => {
      if (isInstanceAdmin) return 'Instance Admin'
      const t = tenant.currentTenant
      if (!t) return 'No tenant selected'
      return t.slug ? `${t.name} (${t.slug})` : t.name
    })
    const currentTenantShort = computed(() => {
      if (isInstanceAdmin) return ''
      const t = tenant.currentTenant
      if (!t) return ''
      return t.slug ? `(${t.slug})` : ''
    })

    async function ensureTenantsLoaded() {
      if (tenant.isLoaded || tenant.isLoading) return
      tenant.isLoading = true
      try {
        tenant.setTenants(await listTenantsForMember())
      } finally {
        tenant.isLoading = false
      }
    }

    function tenantHome(id) {
      return `/t/${id}`
    }

    function buildTargetForTenant(newTenantId) {
      const full = route.fullPath
      if (full.startsWith('/t/')) {
        return full.replace(/^\/t\/[^/]+/, `/t/${newTenantId}`)
      }
      return tenantHome(newTenantId)
    }

    async function switchTenant(newTenantId) {
      if (!newTenantId || newTenantId === tenant.currentTenantId) return
      if (!tenant.tenants.some(t => t.tenantId === newTenantId)) return

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
      console.log('Logging out')
      await auth.logout()
      await router.replace('/signin')
    }

    const clickHandler = ({ target }) => {
      if (!dropdownOpen.value || dropdown.value.contains(target) || trigger.value.contains(target)) return
      closeAll()
    }

    const keyHandler = ({ keyCode }) => {
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

    return {
      tenant,
      isInstanceAdmin,
      dropdownOpen,
      tenantFlyoutOpen,
      trigger,
      dropdown,

      currentTenantName,
      currentTenantLabel,
      currentTenantShort,

      openTenantFlyout,
      switchTenant,
      closeAll,
      logout,
    }
  },
}
</script>
