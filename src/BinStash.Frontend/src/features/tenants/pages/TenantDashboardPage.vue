<template>
  <!-- Page header -->
  <div class="mb-8 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
    <div>
      <h1 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white md:text-[32px]">
        {{ tenantName }}
      </h1>
      <p class="mt-2 text-sm text-slate-500 dark:text-slate-400">
        {{ isTenantAdmin ? 'Tenant administration and overview.' : 'Your workspace overview.' }}
      </p>
    </div>
    <div v-if="isTenantAdmin" class="flex items-center gap-3">
      <router-link
        :to="`/t/${tenantId}/repositories`"
        class="inline-flex items-center gap-2 rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-violet-500/20 transition hover:bg-[#6d78ff]"
      >
        <IconGitBranch class="h-4 w-4 shrink-0" />
        <span>Repositories</span>
      </router-link>
    </div>
  </div>

  <!-- Stats cards -->
  <div class="mb-8 grid grid-cols-12 gap-5">

    <!-- Repositories card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
      <div>
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Repositories</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-[#7C86FF]/10 text-[#7C86FF]">
            <IconGitBranch class="h-5 w-5" />
          </div>
        </div>
        <div class="flex items-end justify-between gap-4">
          <div>
            <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">
              <span v-if="reposLoading" class="text-slate-300 dark:text-slate-600">—</span>
              <span v-else>{{ repos.length }}</span>
            </div>
            <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Total repositories</div>
          </div>
          <router-link
            :to="`/t/${tenantId}/repositories`"
            class="text-xs font-semibold text-[#7C86FF] transition hover:text-[#6974ff]"
          >View all →</router-link>
        </div>
      </div>
    </div>

    <!-- Releases card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
      <div>
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Releases</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-emerald-500/10 text-emerald-500">
            <IconPackage class="h-5 w-5" />
          </div>
        </div>
        <div class="flex items-end justify-between">
          <div>
            <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">
              <span v-if="releasesLoading" class="text-slate-300 dark:text-slate-600">—</span>
              <span v-else>{{ totalReleases }}</span>
            </div>
            <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Across all repositories</div>
          </div>
        </div>
      </div>
    </div>

    <!-- Members card (admin only) -->
    <div v-if="isTenantAdmin" class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
      <div>
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Members</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-sky-500/10 text-sky-500">
            <IconUsers class="h-5 w-5" />
          </div>
        </div>
        <div class="flex items-end justify-between gap-4">
          <div>
            <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">—</div>
            <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Team members</div>
          </div>
          <router-link
            :to="`/t/${tenantId}/members`"
            class="text-xs font-semibold text-[#7C86FF] transition hover:text-[#6974ff]"
          >Manage →</router-link>
        </div>
      </div>
    </div>

    <!-- Storage class card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
      <div>
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Storage</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-indigo-500/10 text-indigo-500">
            <IconDatabase class="h-5 w-5" />
          </div>
        </div>
        <div>
          <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">
            {{ allowedStorageClasses.length }}
          </div>
          <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">
            Storage class<span v-if="allowedStorageClasses.length !== 1">es</span> available
          </div>
        </div>
      </div>
    </div>

  </div>

  <!-- Recent Releases / Activity -->
  <div class="grid grid-cols-12 gap-6">

    <div class="col-span-full xl:col-span-8 space-y-6">
      <!-- Recent releases feed -->
      <DashboardCard
        title="Recent Releases"
        subtitle="Latest across all repositories"
        :icon="IconPackage"
      >
        <!-- Loading -->
        <div v-if="releasesLoading" class="p-8 flex items-center justify-center">
          <svg class="animate-spin w-6 h-6 text-violet-500" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
            <path class="opacity-75 fill-current" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
          </svg>
        </div>

        <!-- Error -->
        <div v-else-if="releasesError" class="p-6">
          <div class="rounded-2xl bg-rose-50 px-4 py-3 text-sm text-rose-700 dark:bg-rose-500/10 dark:text-rose-300">
            {{ releasesError }}
          </div>
        </div>

        <!-- Empty -->
        <div v-else-if="recentReleases.length === 0" class="p-8 text-center">
          <IconPackage class="mx-auto mb-3 h-10 w-10 text-slate-300 dark:text-slate-600" />
          <div class="text-sm font-medium text-slate-500 dark:text-slate-400">No releases yet</div>
          <div class="mt-1 text-xs text-slate-400 dark:text-slate-500">Releases will appear here once created</div>
        </div>

        <!-- List -->
        <div v-else class="divide-y divide-slate-100 dark:divide-white/5">
          <div
            v-for="rel in pagedRecentReleases"
            :key="rel.id"
            class="flex items-center justify-between px-5 py-4 transition hover:bg-slate-50 dark:hover:bg-white/[0.03]"
          >
            <div class="flex items-center gap-3 min-w-0">
              <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-[#7C86FF]/10 text-[#7C86FF]">
                <IconGitBranch class="h-4 w-4" />
              </div>
              <div class="min-w-0">
                <div class="flex flex-wrap items-center gap-2">
                  <div class="truncate text-sm font-semibold text-slate-900 dark:text-white">{{ rel.repoName }}</div>
                  <span class="inline-flex items-center rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-slate-500 dark:bg-[#19284F] dark:text-slate-300">
                    {{ rel.version }}
                  </span>
                  <span
                    v-if="rel.environment"
                    class="inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-medium"
                    :class="rel.environment === 'production'
                      ? 'bg-emerald-500/10 text-emerald-500'
                      : 'bg-sky-500/10 text-sky-500'"
                  >
                    {{ rel.environment }}
                  </span>
                </div>
                <div class="truncate text-xs text-slate-500 dark:text-slate-400">
                  {{ rel.repoName }} · {{ formatDate(rel.createdAt) }}
                </div>
              </div>
            </div>
            <div
              class="ml-4 shrink-0 inline-flex items-center gap-1.5 rounded-full px-2.5 py-1 text-xs font-medium"
              :class="releaseStatusClass(rel)"
            >
              <span class="h-1.5 w-1.5 rounded-full" :class="releaseStatusDotClass(rel)" />
              {{ releaseStatusLabel(rel) }}
            </div>
          </div>

          <div
            v-if="recentReleases.length > recentReleasesPageSize"
            class="border-t border-slate-100 px-5 py-4 dark:border-white/5"
          >
            <div class="flex flex-col gap-3 sm:flex-row sm:items-center sm:justify-between">
              <div class="text-xs text-slate-500 dark:text-slate-400">
                Showing {{ recentReleasesRangeStart }}-{{ recentReleasesRangeEnd }} of {{ recentReleases.length }} recent releases
              </div>
              <PaginationNumeric
                v-model="recentReleasesPage"
                :total-items="recentReleases.length"
                :page-size="recentReleasesPageSize"
                :max-buttons="5"
              />
            </div>
          </div>
        </div>
      </DashboardCard>

      <!-- Role info banner -->
      <div
        class="flex items-start gap-4 rounded-[28px] border p-5"
        :class="isTenantAdmin
          ? 'border-[#3B4285] bg-[#19284F] dark:border-[#3B4285] dark:bg-[#19284F]'
          : 'border-teal-200 bg-teal-50 dark:border-teal-500/30 dark:bg-teal-500/10'"
      >
        <div class="shrink-0 mt-0.5">
          <IconAlertCircleFilled
            :class="isTenantAdmin ? 'text-[#7C86FF]' : 'text-teal-500'"
            class="w-5 h-5"
          />
        </div>
        <div>
          <h3
            class="mb-1 text-sm font-semibold"
            :class="isTenantAdmin ? 'text-[#E0E7FF]' : 'text-teal-800 dark:text-teal-300'"
          >
            {{ isTenantAdmin ? 'You are a Tenant Administrator!' : 'You are a Tenant Member' }}
          </h3>
          <p
            class="text-sm"
            :class="isTenantAdmin ? 'text-[#A3B3FF]' : 'text-teal-700 dark:text-teal-400'"
          >
            <span v-if="isTenantAdmin">
              You have full administrative access to this tenant, including managing repositories,
              members, service accounts, and settings.
            </span>
            <span v-else>
              You have read access to this tenant's repositories and releases.
              Contact your tenant administrator to request additional permissions.
            </span>
          </p>
        </div>
      </div>
    </div>

    <div class="col-span-full xl:col-span-4 space-y-6">
      <!-- Quick actions -->
      <DashboardCard title="Quick Actions" :icon="IconArrowsTransferDown">
        <div class="p-4 space-y-3">
          <DashboardCardAction
            :to="`/t/${tenantId}/repositories`"
            :icon="IconGitBranch"
            title="Browse Repositories"
            subtitle="View and manage your repos"
            icon-bg-class="bg-amber-500/15"
            icon-color-class="text-amber-400"
            icon-hover-bg-class="group-hover:bg-[#BB6A00]"
          />

          <DashboardCardAction
            v-if="isTenantAdmin"
            :to="`/t/${tenantId}/members`"
            :icon="IconUsers"
            title="Manage Members"
            subtitle="Invite and manage team"
            icon-bg-class="bg-emerald-500/15"
            icon-color-class="text-emerald-400"
            icon-hover-bg-class="group-hover:bg-emerald-500"
          />

          <DashboardCardAction
            v-if="isTenantAdmin"
            :to="`/t/${tenantId}/service-accounts`"
            :icon="IconRobot"
            title="Service Accounts"
            subtitle="Manage API access"
            icon-bg-class="bg-sky-500/15"
            icon-color-class="text-sky-400"
            icon-hover-bg-class="group-hover:bg-sky-500"
          />

          <DashboardCardAction
            v-if="!isTenantAdmin"
            to="/select-tenant"
            :icon="IconArrowsTransferDown"
            title="Switch Tenant"
            subtitle="Change your active workspace"
            icon-bg-class="bg-violet-500/15"
            icon-color-class="text-violet-400"
            icon-hover-bg-class="group-hover:bg-violet-500"
          />
        </div>
      </DashboardCard>

      <!-- Repositories panel -->
      <DashboardCard title="Repositories" :icon="IconGitBranch">
        <!-- Loading -->
        <div v-if="reposLoading" class="p-8 flex items-center justify-center">
          <svg class="animate-spin w-6 h-6 text-violet-500" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
            <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
            <path class="opacity-75 fill-current" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
          </svg>
        </div>

        <!-- Empty -->
        <div v-else-if="repos.length === 0" class="p-8 text-center">
          <IconGitBranch class="mx-auto mb-3 h-10 w-10 text-slate-300 dark:text-slate-600" />
          <div class="text-sm font-medium text-slate-500 dark:text-slate-400">No repositories yet</div>
          <router-link
            v-if="isTenantAdmin"
            :to="`/t/${tenantId}/repositories`"
            class="mt-3 inline-flex items-center gap-1 text-xs font-medium text-[#7C86FF] hover:text-[#6974ff]"
          >Create one →</router-link>
        </div>

        <!-- List -->
        <div v-else class="divide-y divide-slate-100 dark:divide-white/5">
          <router-link
            v-for="repo in repos.slice(0, 8)"
            :key="repo.id"
            :to="`/t/${tenantId}/repositories/${repo.id}`"
            class="group flex items-center gap-3 px-5 py-3 transition hover:bg-slate-50 dark:hover:bg-white/[0.03]"
          >
            <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-[#7C86FF]/10 text-[#7C86FF]">
              <IconGitBranch class="h-4 w-4" />
            </div>
            <div class="flex-1 min-w-0">
              <div class="truncate text-sm font-semibold text-slate-900 transition group-hover:text-[#7C86FF] dark:text-white">{{ repo.name }}</div>
              <div class="truncate text-xs text-slate-400 dark:text-slate-500">{{ repo.storageClass }}</div>
            </div>
            <IconChevronRight class="h-4 w-4 shrink-0 text-slate-300 dark:text-slate-600" />
          </router-link>

          <div v-if="repos.length > 8" class="px-5 py-3">
            <router-link
              :to="`/t/${tenantId}/repositories`"
              class="text-xs font-medium text-[#7C86FF] hover:text-[#6974ff]"
            >View all {{ repos.length }} repositories →</router-link>
          </div>
        </div>
      </DashboardCard>
    </div>

  </div>
</template>

<script>
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useTenantStore } from '@/stores/tenant'
import { useAuthStore } from '@/stores/auth'
import { useTenantSettingsStore } from '@/stores/tenantSettings'
import { listRepositoriesWithReleaseStats } from '@/api/repositories'
import PaginationNumeric from '@/components/PaginationNumeric.vue'
import DashboardCard from '@/shared/components/data-display/DashboardCard.vue'
import DashboardCardAction from '@/shared/components/data-display/DashboardCardAction.vue'
import {
  IconGitBranch,
  IconPackage,
  IconUsers,
  IconDatabase,
  IconRobot,
  IconTag,
  IconChevronRight,
  IconAlertCircleFilled,
  IconArrowsTransferDown,
} from '@tabler/icons-vue'

export default {
  name: 'TenantDashboard',
  components: {
    IconGitBranch,
    IconPackage,
    IconUsers,
    IconDatabase,
    IconRobot,
    IconTag,
    IconChevronRight,
    IconAlertCircleFilled,
    IconArrowsTransferDown,
    PaginationNumeric,
    DashboardCard,
    DashboardCardAction,
  },
  setup() {
    const RECENT_RELEASES_HISTORY_LIMIT = 50
    const RECENT_RELEASES_PAGE_SIZE = 5

    const route = useRoute()
    const tenantStore = useTenantStore()
    const authStore = useAuthStore()
    const tenantSettingsStore = useTenantSettingsStore()

    const tenantId = computed(() => route.params.tenantId ?? tenantStore.currentTenantId ?? '')
    const tenantName = computed(
      () => tenantStore.tenants.find(t => t.tenantId === tenantId.value)?.name ?? 'Tenant'
    )
    const isTenantAdmin = computed(() =>
      tenantStore.currentTenant?.role === 'TenantAdmin' || authStore.user?.roles?.includes('InstanceAdmin')
    )
    const allowedStorageClasses = computed(() => tenantSettingsStore.allowedStorageClasses ?? [])

    // Repositories
    const repos = ref([])
    const reposLoading = ref(false)
    const reposError = ref(null)

    // Releases (fetched per repo)
    const releasesLoading = ref(false)
    const releasesError = ref(null)
    const allReleases = ref([])
    const totalReleaseCount = ref(0)
    const recentReleasesPage = ref(1)
    const recentReleasesPageSize = RECENT_RELEASES_PAGE_SIZE

    const totalReleases = computed(() => totalReleaseCount.value)
    const recentReleases = computed(() =>
      allReleases.value
        .slice()
        .sort((a, b) => new Date(b.createdAt) - new Date(a.createdAt))
        .slice(0, RECENT_RELEASES_HISTORY_LIMIT)
    )
    const pagedRecentReleases = computed(() => {
      const start = (recentReleasesPage.value - 1) * recentReleasesPageSize
      return recentReleases.value.slice(start, start + recentReleasesPageSize)
    })
    const recentReleasesRangeStart = computed(() =>
      recentReleases.value.length === 0 ? 0 : (recentReleasesPage.value - 1) * recentReleasesPageSize + 1,
    )
    const recentReleasesRangeEnd = computed(() =>
      Math.min(recentReleasesPage.value * recentReleasesPageSize, recentReleases.value.length),
    )

    async function loadData() {
      reposLoading.value = true
      reposError.value = null
      releasesLoading.value = true
      releasesError.value = null
      allReleases.value = []
      totalReleaseCount.value = 0
      recentReleasesPage.value = 1

      try {
        const data = await listRepositoriesWithReleaseStats(RECENT_RELEASES_HISTORY_LIMIT)
        repos.value = data.map(x => x.repository)
        totalReleaseCount.value = data.reduce((sum, x) => sum + (x.releaseCount ?? 0), 0)
        reposLoading.value = false

        allReleases.value = data.flatMap((x) =>
          (x.recentReleases ?? []).map((r) => ({
            ...r,
            repoName: x.repository.name,
          })),
        )
      } catch (e) {
        if (reposLoading.value) {
          reposError.value = e instanceof Error ? e.message : 'Could not load repositories.'
          reposLoading.value = false
        } else {
          releasesError.value = e instanceof Error ? e.message : 'Could not load releases.'
        }
      } finally {
        releasesLoading.value = false
      }
    }

    function formatDate(iso) {
      if (!iso) return ''
      const d = new Date(iso)
      return d.toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
    }

    // Release status helpers (derive status from release data)
    function getReleaseStatus(rel) {
      if (rel.status) return rel.status
      // Infer status: if created recently (within 5 min), show as running; otherwise success
      const age = Date.now() - new Date(rel.createdAt).getTime()
      if (age < 5 * 60 * 1000) return 'running'
      return 'success'
    }

    function releaseStatusLabel(rel) {
      const status = getReleaseStatus(rel)
      if (status === 'failed') return 'Failed'
      if (status === 'running') return 'Running'
      return 'Success'
    }

    function releaseStatusClass(rel) {
      const status = getReleaseStatus(rel)
      if (status === 'failed') return 'bg-rose-500/10 text-rose-500'
      if (status === 'running') return 'bg-amber-500/10 text-amber-500'
      return 'bg-emerald-500/10 text-emerald-500'
    }

    function releaseStatusDotClass(rel) {
      const status = getReleaseStatus(rel)
      if (status === 'failed') return 'bg-rose-500'
      if (status === 'running') return 'bg-amber-500'
      return 'bg-emerald-500'
    }

    watch(() => tenantId.value, async () => {
      repos.value = []
      allReleases.value = []
      totalReleaseCount.value = 0
      recentReleasesPage.value = 1
      await loadData()
    })

    watch(recentReleases, (releases) => {
      const maxPage = Math.max(1, Math.ceil(releases.length / recentReleasesPageSize))
      if (recentReleasesPage.value > maxPage) {
        recentReleasesPage.value = maxPage
      }
    })

    onMounted(loadData)

    return {
      tenantId,
      tenantName,
      isTenantAdmin,
      allowedStorageClasses,
      repos,
      reposLoading,
      reposError,
      releasesLoading,
      releasesError,
      totalReleases,
      recentReleases,
      pagedRecentReleases,
      recentReleasesPage,
      recentReleasesPageSize,
      recentReleasesRangeStart,
      recentReleasesRangeEnd,
      formatDate,
      releaseStatusLabel,
      releaseStatusClass,
      releaseStatusDotClass,
      // Icons exposed for template :icon bindings
      IconPackage,
      IconGitBranch,
      IconArrowsTransferDown,
      IconUsers,
      IconRobot,
    }
  },
}
</script>