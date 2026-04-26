<template>
  <div class="min-w-fit">
    <!-- Sidebar backdrop (mobile only) -->
    <div
      class="fixed inset-0 z-40 bg-slate-950/60 backdrop-blur-sm lg:hidden lg:z-auto transition-opacity duration-200"
      :class="sidebarOpen ? 'opacity-100' : 'opacity-0 pointer-events-none'"
      aria-hidden="true"
    ></div>

    <!-- Sidebar -->
    <div
      id="sidebar"
      ref="sidebar"
      class="flex lg:flex! flex-col absolute z-40 left-0 top-0 lg:static lg:left-auto lg:top-auto lg:translate-x-0 h-dvh overflow-y-scroll lg:overflow-y-auto no-scrollbar shrink-0 bg-white dark:bg-[#090E1E] text-slate-900 dark:text-white transition-all duration-200 ease-in-out border-r border-slate-200 dark:border-white/5"
      :class="[
        sidebarOpen ? 'translate-x-0' : '-translate-x-64',
        'w-[212px] lg:w-[72px] lg:sidebar-expanded:w-[212px]!'
      ]"
    >
      <!-- Top: Logo + Collapse toggle -->
      <div class="flex items-center px-5 py-5 lg:sidebar-expanded:px-5 lg:px-0 lg:justify-center lg:sidebar-expanded:justify-between">
        <!-- Mobile close button -->
        <button
          ref="trigger"
          class="lg:hidden text-slate-500 transition hover:text-slate-700 dark:text-slate-400 dark:hover:text-white mr-3"
          @click.stop="$emit('close-sidebar')"
          aria-controls="sidebar"
          :aria-expanded="sidebarOpen"
        >
          <span class="sr-only">Close sidebar</span>
          <svg class="w-6 h-6 fill-current" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
            <path d="M10.7 18.7l1.4-1.4L7.8 13H20v-2H7.8l4.3-4.3-1.4-1.4L4 12z" />
          </svg>
        </button>

        <router-link class="block shrink-0" :to="`/t/${tenantId}`">
          <div class="flex h-8 w-8 items-center justify-center rounded-[10px] bg-linear-to-br from-[#615FFF] to-[#9810FA] shadow-lg shadow-violet-500/20">
            <svg class="h-5 w-5 fill-white" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
              <path d="M31.956 14.8C31.372 6.92 25.08.628 17.2.044V5.76a9.04 9.04 0 0 0 9.04 9.04h5.716ZM14.8 26.24v5.716C6.92 31.372.63 25.08.044 17.2H5.76a9.04 9.04 0 0 1 9.04 9.04Zm11.44-9.04h5.716c-.584 7.88-6.876 14.172-14.756 14.756V26.24a9.04 9.04 0 0 1 9.04-9.04ZM.044 14.8C.63 6.92 6.92.628 14.8.044V5.76a9.04 9.04 0 0 1-9.04 9.04H.044Z" />
            </svg>
          </div>
        </router-link>

      </div>

      <!-- Tenant name badge (expanded only) -->
      <div class="px-5 mb-6 lg:px-0 lg:sidebar-expanded:px-5 overflow-hidden">
        <span
          class="inline-flex max-w-full items-center gap-2 rounded-full border border-emerald-500/30 bg-emerald-500/10 px-3 py-1.5 text-xs font-semibold text-emerald-400 dark:text-emerald-300 transition duration-200 truncate lg:opacity-0 lg:sidebar-expanded:opacity-100 lg:h-0 lg:sidebar-expanded:h-auto lg:overflow-hidden"
        >
          <svg class="h-2.5 w-2.5 shrink-0 fill-current" viewBox="0 0 16 16">
            <path d="M8 0a8 8 0 1 0 0 16A8 8 0 0 0 8 0Zm3.5 7.5-4 4a.75.75 0 0 1-1.06 0l-2-2a.75.75 0 1 1 1.06-1.06L7 9.94l3.47-3.47a.75.75 0 1 1 1.06 1.06Z" />
          </svg>
          <span class="truncate">{{ tenantName }}</span>
        </span>
      </div>

      <!-- Navigation -->
      <nav class="flex-1 space-y-6 px-3 lg:px-2 lg:sidebar-expanded:px-3">
        <!-- Workspace group -->
        <div>
          <h3 class="mb-2 px-2 text-[11px] font-medium uppercase tracking-[0.2em] text-slate-400 dark:text-slate-500">
            <span class="hidden lg:block lg:sidebar-expanded:hidden text-center" aria-hidden="true">•••</span>
            <span class="lg:hidden lg:sidebar-expanded:block">Workspace</span>
          </h3>
          <ul class="space-y-0.5">
            <router-link :to="`/t/${tenantId}`" custom v-slot="{ href, navigate, isExactActive }">
              <li>
                <a
                  class="group flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium transition lg:justify-center lg:sidebar-expanded:justify-start"
                  :class="isExactActive
                    ? 'bg-[#19284F] text-white'
                    : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/5'"
                  :href="href"
                  @click="navigate"
                >
                  <IconDashboard
                    class="shrink-0 h-5 w-5"
                    :class="isExactActive ? 'text-[#7C86FF]' : 'text-slate-400 dark:text-slate-500'" />
                  <span class="lg:hidden lg:sidebar-expanded:inline whitespace-nowrap">Overview</span>
                </a>
              </li>
            </router-link>

            <router-link :to="`/t/${tenantId}/repositories`" custom v-slot="{ href, navigate, isActive }">
              <li>
                <a
                  class="group flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium transition lg:justify-center lg:sidebar-expanded:justify-start"
                  :class="isActive
                    ? 'bg-[#19284F] text-white'
                    : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/5'"
                  :href="href"
                  @click="navigate"
                >
                  <IconGitBranch
                    class="shrink-0 h-5 w-5"
                    :class="isActive ? 'text-[#7C86FF]' : 'text-slate-400 dark:text-slate-500'" />
                  <span class="lg:hidden lg:sidebar-expanded:inline whitespace-nowrap">Repositories</span>
                </a>
              </li>
            </router-link>
          </ul>
        </div>

        <!-- Administration group (admin only) -->
        <div v-if="isTenantAdmin">
          <h3 class="mb-2 px-2 text-[11px] font-medium uppercase tracking-[0.2em] text-slate-400 dark:text-slate-500">
            <span class="hidden lg:block lg:sidebar-expanded:hidden text-center" aria-hidden="true">•••</span>
            <span class="lg:hidden lg:sidebar-expanded:block">Administration</span>
          </h3>
          <ul class="space-y-0.5">
            <router-link :to="`/t/${tenantId}/members`" custom v-slot="{ href, navigate, isActive }">
              <li>
                <a
                  class="group flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium transition lg:justify-center lg:sidebar-expanded:justify-start"
                  :class="isActive
                    ? 'bg-[#19284F] text-white'
                    : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/5'"
                  :href="href"
                  @click="navigate"
                >
                  <IconUsers
                    class="shrink-0 h-5 w-5"
                    :class="isActive ? 'text-[#7C86FF]' : 'text-slate-400 dark:text-slate-500'" />
                  <span class="lg:hidden lg:sidebar-expanded:inline whitespace-nowrap">Members</span>
                </a>
              </li>
            </router-link>

            <router-link :to="`/t/${tenantId}/service-accounts`" custom v-slot="{ href, navigate, isActive }">
              <li>
                <a
                  class="group flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium transition lg:justify-center lg:sidebar-expanded:justify-start"
                  :class="isActive
                    ? 'bg-[#19284F] text-white'
                    : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/5'"
                  :href="href"
                  @click="navigate"
                >
                  <IconRobot
                    class="shrink-0 h-5 w-5"
                    :class="isActive ? 'text-[#7C86FF]' : 'text-slate-400 dark:text-slate-500'" />
                  <span class="lg:hidden lg:sidebar-expanded:inline whitespace-nowrap">Service Accounts</span>
                </a>
              </li>
            </router-link>
          </ul>
        </div>

        <!-- Account group -->
        <div>
          <h3 class="mb-2 px-2 text-[11px] font-medium uppercase tracking-[0.2em] text-slate-400 dark:text-slate-500">
            <span class="hidden lg:block lg:sidebar-expanded:hidden text-center" aria-hidden="true">•••</span>
            <span class="lg:hidden lg:sidebar-expanded:block">Account</span>
          </h3>
          <ul class="space-y-0.5">
            <router-link :to="`/t/${tenantId}/settings`" custom v-slot="{ href, navigate, isActive }">
              <li>
                <a
                  class="group flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium transition lg:justify-center lg:sidebar-expanded:justify-start"
                  :class="isActive
                    ? 'bg-[#19284F] text-white'
                    : 'text-slate-600 hover:bg-slate-100 dark:text-slate-300 dark:hover:bg-white/5'"
                  :href="href"
                  @click="navigate"
                >
                  <IconAdjustmentsHorizontal
                    class="shrink-0 h-5 w-5"
                    :class="isActive ? 'text-[#7C86FF]' : 'text-slate-400 dark:text-slate-500'" />
                  <span class="lg:hidden lg:sidebar-expanded:inline whitespace-nowrap">Settings</span>
                </a>
              </li>
            </router-link>
          </ul>
        </div>
      </nav>

      <!-- Bottom section -->
      <div class="mt-auto px-3 pb-5 lg:px-2 lg:sidebar-expanded:px-3">
        <!-- Instance Admin back-link -->
        <div v-if="isInstanceAdmin" class="border-t border-slate-200 pt-3 dark:border-white/10">
          <router-link
            to="/instance/tenants"
            class="group flex items-center gap-3 rounded-xl px-3 py-2 text-sm font-medium text-slate-500 transition hover:bg-slate-100 dark:text-slate-400 dark:hover:bg-white/5 lg:justify-center lg:sidebar-expanded:justify-start"
          >
            <IconArrowLeft class="shrink-0 h-5 w-5 text-slate-400 transition group-hover:text-[#7C86FF]" />
            <span class="lg:hidden lg:sidebar-expanded:inline whitespace-nowrap">Back to Instance</span>
          </router-link>
        </div>

        <!-- Collapse/Expand toggle -->
        <button
          class="mt-2 hidden lg:flex w-full items-center justify-center rounded-xl py-2 text-slate-400 transition hover:bg-slate-100 hover:text-slate-600 dark:text-slate-500 dark:hover:bg-white/5 dark:hover:text-white"
          @click.prevent="sidebarExpanded = !sidebarExpanded"
        >
          <span class="sr-only">{{ sidebarExpanded ? 'Collapse sidebar' : 'Expand sidebar' }}</span>
          <IconChevronsLeft v-if="sidebarExpanded" class="h-5 w-5" />
          <IconChevronsRight v-else class="h-5 w-5" />
        </button>
      </div>
    </div>
  </div>
</template>

<script>
import { ref, computed, onMounted, onUnmounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useTenantStore } from '@/stores/tenant'
import { useAuthStore } from '@/stores/auth'
import {
  sidebarExpandedState,
  setSidebarExpanded,
} from '@/utils/sidebarExpanded'
import {
  IconDashboard,
  IconGitBranch,
  IconUsers,
  IconRobot,
  IconAdjustmentsHorizontal,
  IconArrowLeft,
  IconChevronsLeft,
  IconChevronsRight,
} from '@tabler/icons-vue'

export default {
  name: 'TenantSidebar',
  props: ['sidebarOpen', 'variant'],
  emits: ['close-sidebar'],
  components: {
    IconDashboard,
    IconGitBranch,
    IconUsers,
    IconRobot,
    IconAdjustmentsHorizontal,
    IconArrowLeft,
    IconChevronsLeft,
    IconChevronsRight,
  },
  setup(props, { emit }) {
    const trigger = ref(null)
    const sidebar = ref(null)
    const tenantStore = useTenantStore()
    const authStore = useAuthStore()
    const route = useRoute()

    const tenantId = computed(() => route.params.tenantId ?? tenantStore.currentTenantId ?? '')
    const tenantName = computed(
      () => tenantStore.tenants.find(t => t.tenantId === tenantId.value)?.name ?? 'Tenant'
    )

    const isTenantAdmin = computed(() => {
      const tenantRole = tenantStore.currentTenant?.role
      const globalRoles = authStore.user?.roles ?? []
      return tenantRole === 'TenantAdmin' || globalRoles.includes('InstanceAdmin')
    })

    const isInstanceAdmin = computed(() =>
      (authStore.user?.roles ?? []).includes('InstanceAdmin'),
    )

    const sidebarExpanded = sidebarExpandedState

    const clickHandler = ({ target }) => {
      if (!sidebar.value || !trigger.value) return
      if (!props.sidebarOpen || sidebar.value.contains(target) || trigger.value.contains(target)) return
      emit('close-sidebar')
    }

    const keyHandler = ({ keyCode }) => {
      if (!props.sidebarOpen || keyCode !== 27) return
      emit('close-sidebar')
    }

    onMounted(() => {
      document.addEventListener('click', clickHandler)
      document.addEventListener('keydown', keyHandler)
      setSidebarExpanded(sidebarExpanded.value)
    })

    onUnmounted(() => {
      document.removeEventListener('click', clickHandler)
      document.removeEventListener('keydown', keyHandler)
    })

    watch(sidebarExpanded, () => {
      setSidebarExpanded(sidebarExpanded.value)
    })

    return {
      trigger,
      sidebar,
      sidebarExpanded,
      tenantId,
      tenantName,
      isTenantAdmin,
      isInstanceAdmin,
    }
  },
}
</script>