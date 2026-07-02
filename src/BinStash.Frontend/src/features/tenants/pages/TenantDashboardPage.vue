<template>
  <div>
  <!-- Page header -->
  <div class="mb-8 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
    <div>
      <h1 class="text-3xl font-bold tracking-tight text-ink-strong">
        {{ tenantName }}
      </h1>
      <p class="mt-1 text-sm text-ink-muted">
        {{ isTenantAdmin ? 'Tenant administration and overview.' : 'Your workspace overview.' }}
      </p>
    </div>
    <div v-if="isTenantAdmin" class="flex items-center gap-3">
      <BaseButton :to="`/t/${tenantId}/repositories`" :icon="IconGitBranch">Repositories</BaseButton>
    </div>
  </div>

  <!-- Stats cards -->
  <div class="mb-8 grid gap-5 sm:grid-cols-2 xl:grid-cols-4">

    <!-- Repositories card -->
    <div class="rounded-card border border-hairline bg-card p-5">
      <div class="mb-4 flex items-center gap-3">
        <div class="flex size-10 items-center justify-center rounded-full bg-accent-soft text-accent">
          <IconGitBranch class="size-5" />
        </div>
        <div class="text-xs font-semibold uppercase tracking-wide text-ink-subtle">Repositories</div>
      </div>
      <div class="text-[2rem] font-bold leading-none text-ink-strong">
        <span v-if="reposLoading" class="text-ink-subtle">—</span>
        <span v-else>{{ repos.length }}</span>
      </div>
      <div class="mt-2 flex items-end justify-between gap-4">
        <div class="text-sm text-ink-muted">Total repositories</div>
        <router-link
          :to="`/t/${tenantId}/repositories`"
          class="text-xs font-semibold text-accent transition hover:brightness-110"
        >View all →</router-link>
      </div>
    </div>

    <!-- Releases card -->
    <div class="rounded-card border border-hairline bg-card p-5">
      <div class="mb-4 flex items-center gap-3">
        <div class="flex size-10 items-center justify-center rounded-full bg-success-soft text-success">
          <IconPackage class="size-5" />
        </div>
        <div class="text-xs font-semibold uppercase tracking-wide text-ink-subtle">Releases</div>
      </div>
      <div class="text-[2rem] font-bold leading-none text-ink-strong">
        <span v-if="releasesLoading" class="text-ink-subtle">—</span>
        <span v-else>{{ totalReleases }}</span>
      </div>
      <div class="mt-2 text-sm text-ink-muted">Across all repositories</div>
    </div>

    <!-- Members card (admin only) -->
    <div v-if="isTenantAdmin" class="rounded-card border border-hairline bg-card p-5">
      <div class="mb-4 flex items-center gap-3">
        <div class="flex size-10 items-center justify-center rounded-full bg-accent-soft text-accent">
          <IconUsers class="size-5" />
        </div>
        <div class="text-xs font-semibold uppercase tracking-wide text-ink-subtle">Members</div>
      </div>
      <div class="text-[2rem] font-bold leading-none text-ink-strong">—</div>
      <div class="mt-2 flex items-end justify-between gap-4">
        <div class="text-sm text-ink-muted">Team members</div>
        <router-link
          :to="`/t/${tenantId}/members`"
          class="text-xs font-semibold text-accent transition hover:brightness-110"
        >Manage →</router-link>
      </div>
    </div>

    <!-- Storage class card -->
    <div class="rounded-card border border-hairline bg-card p-5">
      <div class="mb-4 flex items-center gap-3">
        <div class="flex size-10 items-center justify-center rounded-full bg-accent-soft text-accent">
          <IconDatabase class="size-5" />
        </div>
        <div class="text-xs font-semibold uppercase tracking-wide text-ink-subtle">Storage</div>
      </div>
      <div class="text-[2rem] font-bold leading-none text-ink-strong">
        {{ allowedStorageClasses.length }}
      </div>
      <div class="mt-2 text-sm text-ink-muted">
        Storage class<span v-if="allowedStorageClasses.length !== 1">es</span> available
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
          <Spinner :size="24" color="var(--color-accent)" />
        </div>

        <!-- Error -->
        <div v-else-if="releasesError" class="p-6">
          <div class="rounded-2xl bg-danger-soft px-4 py-3 text-sm text-danger">
            {{ releasesError }}
          </div>
        </div>

        <!-- Empty -->
        <EmptyState
          v-else-if="recentReleases.length === 0"
          :icon="IconPackage"
          title="No releases yet"
          description="Releases will appear here once created"
        />

        <!-- List -->
        <div v-else class="divide-y divide-hairline">
          <div
            v-for="rel in pagedRecentReleases"
            :key="rel.id"
            class="flex items-center justify-between px-5 py-4 transition hover:bg-raised"
          >
            <div class="flex items-center gap-3 min-w-0">
              <component :is="releaseStatusIcon(rel)" class="size-5 shrink-0" :class="releaseStatusTextClass(rel)" />
              <div class="min-w-0">
                <div class="flex flex-wrap items-center gap-2">
                  <div class="truncate text-sm font-semibold text-ink-strong">{{ rel.repoName }}</div>
                  <span class="inline-flex items-center rounded-full bg-hairline px-2 py-0.5 text-[11px] font-medium text-ink-muted">
                    {{ rel.version }}
                  </span>
                  <span
                    v-if="rel.environment"
                    class="inline-flex items-center rounded-full px-2 py-0.5 text-[11px] font-medium"
                    :class="rel.environment === 'production'
                      ? 'bg-success-soft text-success'
                      : 'bg-accent-soft text-accent'"
                  >
                    {{ rel.environment }}
                  </span>
                </div>
                <div class="mt-0.5 truncate text-xs text-ink-muted">
                  {{ formatDate(rel.createdAt) }}
                </div>
              </div>
            </div>
            <div class="ml-4 flex shrink-0 items-center gap-2">
              <span class="text-sm font-medium" :class="releaseStatusTextClass(rel)">{{ releaseStatusLabel(rel) }}</span>
              <IconChevronRight class="size-4 text-ink-subtle" />
            </div>
          </div>

          <div
            v-if="recentReleases.length > recentReleasesPageSize"
            class="border-t border-hairline px-5 py-4"
          >
            <BasePagination
              v-model="recentReleasesPage"
              :total-items="recentReleases.length"
              :page-size="recentReleasesPageSize"
              :max-buttons="5"
            />
          </div>
        </div>
      </DashboardCard>

      <!-- Role info banner -->
      <div
        class="flex items-start gap-4 rounded-card border p-5"
        :class="isTenantAdmin ? 'border-accent/25 bg-accent-soft' : 'border-success/25 bg-success-soft'"
      >
        <IconAlertCircleFilled
          class="mt-0.5 size-5 shrink-0"
          :class="isTenantAdmin ? 'text-accent' : 'text-success'"
        />
        <div>
          <h3 class="mb-1 text-sm font-semibold text-ink-strong">
            {{ isTenantAdmin ? 'You are a Tenant Administrator!' : 'You are a Tenant Member' }}
          </h3>
          <p class="text-sm text-ink-muted">
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
      <DashboardCard title="Quick Actions" :icon="IconBolt">
        <div class="p-4 space-y-3">
          <DashboardCardAction
            :to="`/t/${tenantId}/repositories`"
            :icon="IconGitBranch"
            title="Browse Repositories"
            subtitle="View and manage your repos"
            icon-bg-class="bg-accent-soft"
            icon-color-class="text-accent"
            icon-hover-bg-class="group-hover:bg-[#BB6A00]"
          />

          <DashboardCardAction
            v-if="isTenantAdmin"
            :to="`/t/${tenantId}/members`"
            :icon="IconUsers"
            title="Manage Members"
            subtitle="Invite and manage team"
            icon-bg-class="bg-accent-soft"
            icon-color-class="text-accent"
            icon-hover-bg-class="group-hover:bg-accent"
          />

          <DashboardCardAction
            v-if="isTenantAdmin"
            :to="`/t/${tenantId}/service-accounts`"
            :icon="IconRobot"
            title="Service Accounts"
            subtitle="Manage API access"
            icon-bg-class="bg-accent-soft"
            icon-color-class="text-accent"
            icon-hover-bg-class="group-hover:bg-accent"
          />

          <DashboardCardAction
            v-if="!isTenantAdmin"
            to="/select-tenant"
            :icon="IconArrowsTransferDown"
            title="Switch Tenant"
            subtitle="Change your active workspace"
            icon-bg-class="bg-accent-soft"
            icon-color-class="text-accent"
            icon-hover-bg-class="group-hover:bg-accent"
          />
        </div>
      </DashboardCard>

      <!-- Repositories panel -->
      <DashboardCard title="Repositories" :icon="IconGitBranch">
        <!-- Loading -->
        <div v-if="reposLoading" class="p-8 flex items-center justify-center">
          <Spinner :size="24" color="var(--color-accent)" />
        </div>

        <!-- Empty -->
        <EmptyState
          v-else-if="repos.length === 0"
          :icon="IconGitBranch"
          title="No repositories yet"
        >
          <BaseButton v-if="isTenantAdmin" :to="`/t/${tenantId}/repositories`" size="sm" :icon="IconGitBranch">Create one</BaseButton>
        </EmptyState>

        <!-- List -->
        <div v-else class="divide-y divide-hairline">
          <router-link
            v-for="repo in repos.slice(0, 8)"
            :key="repo.id"
            :to="`/t/${tenantId}/repositories/${repo.id}`"
            class="group flex items-center gap-3 px-5 py-3 transition hover:bg-raised"
          >
            <div class="flex size-9 shrink-0 items-center justify-center rounded-xl bg-accent-soft text-accent">
              <IconGitBranch class="h-4 w-4" />
            </div>
            <div class="flex-1 min-w-0">
              <div class="truncate text-sm font-semibold text-ink-strong transition group-hover:text-accent">{{ repo.name }}</div>
              <div class="truncate text-xs text-ink-subtle">{{ repo.storageClass }}</div>
            </div>
            <IconChevronRight class="h-4 w-4 shrink-0 text-ink-subtle" />
          </router-link>

          <div v-if="repos.length > 8" class="px-5 py-3">
            <router-link
              :to="`/t/${tenantId}/repositories`"
              class="text-xs font-medium text-accent transition hover:brightness-110"
            >View all {{ repos.length }} repositories →</router-link>
          </div>
        </div>
      </DashboardCard>
    </div>

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
import DashboardCard from '@/shared/components/data-display/DashboardCard.vue'
import DashboardCardAction from '@/shared/components/data-display/DashboardCardAction.vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BasePagination, StatCard, PageHeader, BaseButton, EmptyState } from '@/shared/components/ui'
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
  IconBolt,
  IconCircleCheckFilled,
  IconClockFilled,
  IconCircleXFilled,
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
    DashboardCard,
    DashboardCardAction,
    Spinner,
    BasePagination,
    StatCard,
    PageHeader,
    BaseButton,
    EmptyState,
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
      if (status === 'failed') return 'bg-danger-soft text-danger'
      if (status === 'running') return 'bg-warning-soft text-warning'
      return 'bg-success-soft text-success'
    }

    function releaseStatusDotClass(rel) {
      const status = getReleaseStatus(rel)
      if (status === 'failed') return 'bg-danger'
      if (status === 'running') return 'bg-warning'
      return 'bg-success'
    }

    function releaseStatusIcon(rel) {
      const status = getReleaseStatus(rel)
      if (status === 'failed') return IconCircleXFilled
      if (status === 'running') return IconClockFilled
      return IconCircleCheckFilled
    }

    function releaseStatusTextClass(rel) {
      const status = getReleaseStatus(rel)
      if (status === 'failed') return 'text-danger'
      if (status === 'running') return 'text-warning'
      return 'text-success'
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
      releaseStatusIcon,
      releaseStatusTextClass,
      // Icons exposed for template :icon bindings
      IconPackage,
      IconGitBranch,
      IconBolt,
      IconArrowsTransferDown,
      IconUsers,
      IconRobot,
    }
  },
}
</script>