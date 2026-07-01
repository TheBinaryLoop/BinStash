<template>
  <div class="space-y-6">
    <Breadcrumbs
      :items="[
        { label: 'Repositories', to: `/t/${tenantId}/repositories` },
        { label: repo?.name ?? '…' },
      ]"
    />

    <div v-if="repoLoading" class="space-y-6">
      <BaseCard accent>
        <div class="space-y-4">
          <Skeleton width="8rem" height="0.75rem" />
          <Skeleton width="16rem" height="2.5rem" />
          <Skeleton width="100%" height="4rem" rounded="card" />
        </div>
      </BaseCard>
      <div class="grid grid-cols-1 xl:grid-cols-3 gap-6">
        <Skeleton class="xl:col-span-2" width="100%" height="24rem" rounded="card" />
        <Skeleton width="100%" height="24rem" rounded="card" />
      </div>
    </div>

    <div v-else-if="repoError" class="rounded-card border border-danger/20 bg-danger-soft px-5 py-4 text-sm text-danger">
      {{ repoError }}
    </div>

    <template v-else-if="repo">
      <BaseCard accent>
        <div class="flex flex-col gap-4">
          <div class="flex items-start gap-3">
            <div class="flex size-12 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent">
              <IconGitBranch class="size-6" />
            </div>
            <div class="min-w-0">
              <h1 class="truncate text-2xl font-bold text-ink-strong">{{ repo.name }}</h1>
              <p class="mt-1.5 max-w-3xl text-sm leading-6 text-ink-muted">
                {{ repo.description || 'A release repository for ingesting and managing versioned package snapshots.' }}
              </p>
            </div>
          </div>
          <div class="flex flex-wrap items-center gap-x-6 gap-y-2 border-t border-hairline pt-4 text-sm">
            <span class="inline-flex items-center gap-1.5 text-ink-muted">
              <IconDatabase class="size-4 text-ink-subtle" /> Storage
              <span class="font-medium text-ink-strong">{{ repo.storageClass }}</span>
            </span>
            <span class="inline-flex items-center gap-1.5 text-ink-muted">
              <IconPackage class="size-4 text-ink-subtle" />
              <span class="font-medium text-ink-strong">{{ totalReleaseCount }}</span>
              release{{ totalReleaseCount === 1 ? '' : 's' }}
            </span>
            <span class="inline-flex items-center gap-1.5 text-ink-muted">
              <IconTag class="size-4 text-ink-subtle" /> Latest
              <span class="font-medium text-ink-strong">{{ latestRelease?.version ?? '—' }}</span>
            </span>
            <span class="inline-flex items-center gap-1.5 text-ink-muted">
              <IconCalendar class="size-4 text-ink-subtle" /> Created
              <span class="font-medium text-ink-strong">{{ formatDate(repo.createdAt) }}</span>
            </span>
          </div>
        </div>
      </BaseCard>

      <Tabs :tabs="tabItems" v-model="activeTab" />

      <section v-if="activeTab === 'details'" class="grid grid-cols-1 gap-6 xl:grid-cols-3">
        <div class="space-y-6 xl:col-span-2">
          <BaseCard :padded="false">
            <template #header>
              <h2 class="font-semibold text-ink-strong">About</h2>
            </template>
            <dl class="divide-y divide-hairline">
              <div class="flex items-start justify-between gap-4 px-5 py-3.5">
                <dt class="text-sm text-ink-muted">Repository ID</dt>
                <dd class="flex items-center gap-2 text-right font-mono text-xs text-ink-strong">
                  <span class="break-all">{{ repo.id }}</span>
                  <button type="button" class="shrink-0 text-ink-subtle transition hover:text-ink-strong" :aria-label="copiedId ? 'Copied' : 'Copy repository ID'" @click="copyRepoId">
                    <IconCheck v-if="copiedId" class="size-4 text-success" />
                    <IconCopy v-else class="size-4" />
                  </button>
                </dd>
              </div>
              <div class="flex items-center justify-between gap-4 px-5 py-3.5">
                <dt class="text-sm text-ink-muted">Created</dt>
                <dd class="text-sm font-medium text-ink-strong">{{ formatDateTime(repo.createdAt) }}</dd>
              </div>
              <div class="flex items-center justify-between gap-4 px-5 py-3.5">
                <dt class="text-sm text-ink-muted">Storage class</dt>
                <dd class="text-sm font-medium text-ink-strong">{{ repo.storageClass }}</dd>
              </div>
              <div class="flex items-start justify-between gap-4 px-5 py-3.5">
                <dt class="text-sm text-ink-muted">Latest release</dt>
                <dd class="text-right">
                  <div class="text-sm font-medium text-ink-strong">{{ latestRelease?.version ?? 'No releases yet' }}</div>
                  <div v-if="latestRelease" class="text-xs text-ink-muted">{{ formatDateTime(latestRelease.createdAt) }}</div>
                </dd>
              </div>
              <div class="px-5 py-3.5">
                <dt class="text-sm text-ink-muted">Description</dt>
                <dd class="mt-1.5 text-sm leading-6 text-ink-strong">{{ repo.description || 'No description provided.' }}</dd>
              </div>
            </dl>
          </BaseCard>
        </div>

        <div class="space-y-6">
          <BaseCard :padded="false">
            <template #header>
              <div class="flex items-center gap-2.5">
                <IconAdjustmentsHorizontal class="size-4 text-ink-subtle" />
                <h3 class="font-semibold text-ink-strong">Deduplication</h3>
              </div>
            </template>
            <div v-if="configLoading" class="flex items-center justify-center py-12"><Spinner :size="28" color="var(--color-accent)" /></div>
            <div v-else-if="configError" class="p-5"><div class="rounded-control border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">{{ configError }}</div></div>
            <dl v-else-if="config" class="divide-y divide-hairline">
              <div class="flex items-center justify-between gap-4 px-5 py-3"><dt class="text-sm text-ink-muted">Chunker</dt><dd class="text-sm font-medium text-ink-strong">{{ config.dedupeConfig.chunker }}</dd></div>
              <div v-if="config.dedupeConfig.minChunkSize != null" class="flex items-center justify-between gap-4 px-5 py-3"><dt class="text-sm text-ink-muted">Min chunk size</dt><dd class="text-sm font-medium text-ink-strong">{{ formatBytes(config.dedupeConfig.minChunkSize) }}</dd></div>
              <div v-if="config.dedupeConfig.avgChunkSize != null" class="flex items-center justify-between gap-4 px-5 py-3"><dt class="text-sm text-ink-muted">Average chunk size</dt><dd class="text-sm font-medium text-ink-strong">{{ formatBytes(config.dedupeConfig.avgChunkSize) }}</dd></div>
              <div v-if="config.dedupeConfig.maxChunkSize != null" class="flex items-center justify-between gap-4 px-5 py-3"><dt class="text-sm text-ink-muted">Max chunk size</dt><dd class="text-sm font-medium text-ink-strong">{{ formatBytes(config.dedupeConfig.maxChunkSize) }}</dd></div>
              <div v-if="config.dedupeConfig.shiftCount != null" class="flex items-center justify-between gap-4 px-5 py-3"><dt class="text-sm text-ink-muted">Shift count</dt><dd class="text-sm font-medium text-ink-strong">{{ config.dedupeConfig.shiftCount }}</dd></div>
              <div v-if="config.dedupeConfig.boundaryCheckBytes != null" class="flex items-center justify-between gap-4 px-5 py-3"><dt class="text-sm text-ink-muted">Boundary check</dt><dd class="text-sm font-medium text-ink-strong">{{ formatBytes(config.dedupeConfig.boundaryCheckBytes) }}</dd></div>
            </dl>
            <div v-else class="p-5 text-sm text-ink-subtle">No deduplication configuration is available for this repository.</div>
          </BaseCard>
        </div>
      </section>

      <section v-if="activeTab === 'releases'">
        <div class="rounded-card border border-hairline bg-card overflow-hidden">
          <div class="border-b border-hairline px-5 py-4">
            <div class="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
              <div>
                <h2 class="text-lg font-semibold text-ink-strong">Release activity</h2>
                <p class="mt-1 text-sm text-ink-muted">
                  Showing {{ releases.length }} of {{ filteredReleaseCount }} matching releases across {{ totalReleaseCount }} total.
                </p>
              </div>
              <div class="grid gap-3 sm:grid-cols-3 xl:min-w-2xl">
                <BaseInput
                  v-model="releaseSearch"
                  type="search"
                  placeholder="Search by version or notes…"
                  :prefix-icon="IconSearch"
                />
                <BaseSelect
                  v-model="releaseDateRange"
                  :options="[
                    { value: 'all', label: 'All time' },
                    { value: '30d', label: 'Last 30 days' },
                    { value: '90d', label: 'Last 90 days' },
                  ]"
                />
                <BaseSelect
                  v-model="releaseSortKey"
                  :options="[
                    { value: 'createdAt:DESC', label: 'Newest first' },
                    { value: 'createdAt:ASC', label: 'Oldest first' },
                    { value: 'version:ASC', label: 'Version A → Z' },
                    { value: 'version:DESC', label: 'Version Z → A' },
                  ]"
                />
              </div>
            </div>
          </div>

          <div v-if="releasesLoading" class="flex items-center justify-center py-16">
            <Spinner :size="32" color="var(--color-accent)" />
          </div>

          <EmptyState
            v-else-if="releases.length === 0"
            :icon="IconPackage"
            :title="releaseSearch || releaseDateRange !== 'all' ? 'No matching releases' : 'No releases yet'"
            :description="releaseSearch || releaseDateRange !== 'all' ? 'Try adjusting the filters to broaden your search.' : 'New ingested releases will appear here automatically.'"
          />

          <div v-else class="divide-y divide-hairline">
            <article v-for="rel in releases" :key="rel.id" class="group px-5 py-4 transition hover:bg-raised">
              <div class="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                <div class="min-w-0 flex items-start gap-3">
                  <div class="mt-0.5 flex size-10 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent">
                    <IconPackage class="size-5" />
                  </div>
                  <div class="min-w-0">
                    <div class="flex flex-wrap items-center gap-2">
                      <router-link :to="releaseLink(rel.id)" class="text-sm font-semibold text-ink-strong transition group-hover:text-accent">
                        {{ rel.version }}
                      </router-link>
                      <span class="text-xs text-ink-muted">{{ formatDate(rel.createdAt) }}</span>
                      <span class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-semibold"
                        :class="scoreBadgeClasses(getReleaseScore(rel))">
                        Score {{ formatReleaseScore(getReleaseScore(rel)) }}
                      </span>
                    </div>
                    <p class="mt-1 line-clamp-1 text-sm text-ink-muted">
                      {{ rel.notes || 'No release notes were provided for this version.' }}
                    </p>
                  </div>
                </div>
                <div class="flex items-center gap-2 lg:justify-end">
                  <BaseButton variant="secondary" size="sm" :to="releaseLink(rel.id)">Details</BaseButton>
                  <BaseButton size="sm" :icon="IconDownload" :href="releaseDownloadUrl(rel.id)" target="_blank">Download</BaseButton>
                </div>
              </div>
            </article>
          </div>

          <div class="flex flex-col gap-3 border-t border-hairline px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
            <div class="text-xs text-ink-muted">Page {{ releasePage }} · {{ filteredReleaseCount }} matching release<span v-if="filteredReleaseCount !== 1">s</span></div>
            <div class="flex items-center gap-2">
              <BaseButton variant="secondary" size="sm" :disabled="!releaseHasPreviousPage || releasesLoading" @click="goToPreviousReleasePage">Previous</BaseButton>
              <BaseButton variant="secondary" size="sm" :disabled="!releaseHasNextPage || releasesLoading" @click="goToNextReleasePage">Next</BaseButton>
            </div>
          </div>
        </div>
      </section>

      <section v-if="activeTab === 'access'" class="rounded-card border border-hairline bg-card overflow-hidden">
        <div class="border-b border-hairline px-5 py-4 flex items-center gap-3">
          <div class="flex size-9 items-center justify-center rounded-full bg-accent-soft text-accent">
            <IconLock class="h-4 w-4" />
          </div>
          <h3 class="font-semibold text-ink-strong">Access control</h3>
        </div>
        <div v-if="accessLoading" class="flex items-center justify-center py-16"><Spinner :size="32" color="var(--color-accent)" /></div>
        <div v-else-if="accessError" class="p-5"><div class="rounded-control border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">{{ accessError }}</div></div>
        <EmptyState
          v-else-if="accessList.length === 0"
          :icon="IconLock"
          title="No access entries configured."
        />
        <div v-else class="divide-y divide-hairline">
          <div v-for="entry in accessList" :key="`${entry.subjectType}-${entry.subjectId}`" class="flex flex-col gap-3 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
            <div class="flex items-center gap-3 min-w-0">
              <div class="flex size-10 items-center justify-center rounded-full bg-accent-soft text-accent">
                <component :is="entry.subjectType === 0 ? IconUser : IconRobot" class="size-5" />
              </div>
              <div class="min-w-0">
                <div class="truncate text-sm font-semibold text-ink-strong">{{ entry.subjectType === 0 ? 'User principal' : 'Service account' }}</div>
                <div class="truncate font-mono text-xs text-ink-muted">{{ entry.subjectId }}</div>
              </div>
            </div>
            <div class="flex items-center gap-3 sm:justify-end">
              <BaseBadge tone="accent">{{ entry.role }}</BaseBadge>
              <span class="text-xs text-ink-muted">{{ formatDate(entry.grantedAt) }}</span>
            </div>
          </div>
        </div>
      </section>
    </template>
  </div>
</template>

<script setup>
import { computed, onMounted, ref, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useTenantStore } from '@/stores/tenant'
import { getRepositoryAccess, getRepositoryConfig, getRepositoryDetailWithReleases } from '@/api/repositories'
import Tabs from '@/shared/components/navigation/Tabs.vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import {
  Breadcrumbs,
  BaseCard,
  BaseButton,
  BaseBadge,
  BaseInput,
  BaseSelect,
  Skeleton,
  EmptyState,
} from '@/shared/components/ui'
import {
  IconAdjustmentsHorizontal,
  IconCalendar,
  IconCheck,
  IconCopy,
  IconDatabase,
  IconDownload,
  IconGitBranch,
  IconInfoCircle,
  IconLock,
  IconPackage,
  IconRobot,
  IconSearch,
  IconTag,
  IconUser,
} from '@tabler/icons-vue'
import { calculateReleaseScore, getReleaseScoreBadgeClasses } from '@/utils/releaseScore'

const route = useRoute()
const tenantStore = useTenantStore()

const tenantId = computed(() => route.params.tenantId ?? tenantStore.currentTenantId ?? '')
const repoId = computed(() => route.params.repoId)
const isTenantAdmin = computed(() => tenantStore.currentTenant?.role === 'TenantAdmin')

const tabs = computed(() => {
  const base = [
    { key: 'releases', label: 'Releases', icon: IconPackage },
    { key: 'details', label: 'Details', icon: IconInfoCircle },
  ]
  if (isTenantAdmin.value) base.push({ key: 'access', label: 'Access Control', icon: IconLock })
  return base
})

const tabItems = computed(() => tabs.value.map((t) => ({ id: t.key, label: t.label, icon: t.icon })))

const activeTab = ref('releases')
const repo = ref(null)
const repoLoading = ref(false)
const repoError = ref(null)

const releases = ref([])
const releasesLoading = ref(false)
const releaseSearch = ref('')
const releasePage = ref(1)
const releasePageSize = 10
const releaseCursor = ref(null)
const releaseEndCursor = ref(null)
const releasePageCursors = ref([null])
const releaseHasNextPage = ref(false)
const releaseSortKey = ref('createdAt:DESC')
const releaseDateRange = ref('all')
const totalReleaseCount = ref(0)
const filteredReleaseCount = ref(0)
const latestRelease = ref(null)
const releaseRequestId = ref(0)

const config = ref(null)
const configLoading = ref(false)
const configError = ref(null)

const accessList = ref([])
const accessLoading = ref(false)
const accessError = ref(null)

const releaseSortBy = computed(() => releaseSortKey.value.split(':')[0])
const releaseSortDirection = computed(() => releaseSortKey.value.split(':')[1] ?? 'DESC')
const releaseHasPreviousPage = computed(() => releasePage.value > 1)

function resetReleasePaging() {
  releasePage.value = 1
  releaseCursor.value = null
  releaseEndCursor.value = null
  releasePageCursors.value = [null]
  releaseHasNextPage.value = false
}

async function loadRepositoryDetail() {
  const requestId = ++releaseRequestId.value
  const activeCursor = releaseCursor.value ?? null

  repoLoading.value = repo.value == null
  repoError.value = null
  releasesLoading.value = true

  try {
    const data = await getRepositoryDetailWithReleases(repoId.value, {
      search: releaseSearch.value,
      pageSize: releasePageSize,
      after: releaseCursor.value,
      sortBy: releaseSortBy.value,
      sortDirection: releaseSortDirection.value,
      dateRange: releaseDateRange.value,
    })

    if (requestId !== releaseRequestId.value) return

    repo.value = data.repository
    releases.value = data.releases
    totalReleaseCount.value = data.totalReleaseCount
    filteredReleaseCount.value = data.filteredReleaseCount ?? data.releases.length
    latestRelease.value = data.latestRelease
    releasePageCursors.value[releasePage.value - 1] = activeCursor
    releaseHasNextPage.value = Boolean(data.pageInfo?.hasNextPage)
    releaseEndCursor.value = data.pageInfo?.endCursor ?? null
  } catch (e) {
    if (requestId !== releaseRequestId.value) return

    repoError.value = e instanceof Error ? e.message : 'Could not load repository.'
    releases.value = []
    totalReleaseCount.value = 0
    filteredReleaseCount.value = 0
    latestRelease.value = null
    releaseHasNextPage.value = false
    releaseEndCursor.value = null
  } finally {
    if (requestId !== releaseRequestId.value) return

    repoLoading.value = false
    releasesLoading.value = false
  }
}

async function loadConfig() {
  if (config.value) return
  configLoading.value = true
  configError.value = null
  try {
    config.value = await getRepositoryConfig(repoId.value)
  } catch (e) {
    configError.value = e instanceof Error ? e.message : 'Could not load configuration.'
  } finally {
    configLoading.value = false
  }
}

async function loadAccess() {
  if (accessList.value.length > 0) return
  accessLoading.value = true
  accessError.value = null
  try {
    accessList.value = await getRepositoryAccess(repoId.value)
  } catch (e) {
    accessError.value = e instanceof Error ? e.message : 'Could not load access control.'
  } finally {
    accessLoading.value = false
  }
}

function resetFiltersAndReload() {
  releaseCursor.value = null
  releasePageCursors.value = [null]
  releaseEndCursor.value = null
  releaseHasNextPage.value = false
  if (releasePage.value !== 1) {
    releasePage.value = 1
    return
  }
  if (!repoId.value) return
  loadRepositoryDetail()
}

function goToNextReleasePage() {
  if (!releaseHasNextPage.value || releasesLoading.value || !releaseEndCursor.value) return
  const targetPage = releasePage.value + 1
  releasePageCursors.value = releasePageCursors.value.slice(0, releasePage.value)
  releasePageCursors.value[targetPage - 1] = releaseEndCursor.value
  releaseCursor.value = releaseEndCursor.value
  releasePage.value = targetPage
}

function goToPreviousReleasePage() {
  if (releasePage.value <= 1 || releasesLoading.value) return
  const targetPage = releasePage.value - 1
  releaseCursor.value = releasePageCursors.value[targetPage - 1] ?? null
  releasePage.value = targetPage
}

function releaseLink(releaseId) {
  return `/t/${tenantId.value}/repositories/${repoId.value}/releases/${releaseId}`
}

function releaseDownloadUrl(releaseId) {
  return `/api/tenants/${tenantId.value}/repositories/${repoId.value}/releases/${releaseId}/download`
}

function formatDate(iso) {
  if (!iso) return ''
  return new Date(iso).toLocaleDateString(undefined, { year: 'numeric', month: 'short', day: 'numeric' })
}

function formatDateTime(iso) {
  if (!iso) return '—'
  return new Date(iso).toLocaleString(undefined, {
    year: 'numeric', month: 'short', day: 'numeric', hour: '2-digit', minute: '2-digit',
  })
}

function formatBytes(bytes) {
  if (bytes == null) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB']
  let i = 0
  let val = bytes
  while (val >= 1024 && i < units.length - 1) {
    val /= 1024
    i += 1
  }
  return `${val % 1 === 0 ? val : val.toFixed(1)} ${units[i]}`
}

function getReleaseScore(release) {
  return calculateReleaseScore(release?.metrics)
}

function formatReleaseScore(score) {
  return score == null ? '—' : `${score}/100`
}

function scoreBadgeClasses(score) {
  return getReleaseScoreBadgeClasses(score)
}

const copiedId = ref(false)
async function copyRepoId() {
  try {
    await navigator.clipboard.writeText(repo.value?.id ?? '')
    copiedId.value = true
    setTimeout(() => (copiedId.value = false), 1500)
  } catch {
    /* clipboard unavailable */
  }
}

watch(activeTab, (tab) => {
  if (tab === 'details') loadConfig()
  if (tab === 'access') loadAccess()
})

watch(repoId, () => {
  repo.value = null
  releases.value = []
  totalReleaseCount.value = 0
  filteredReleaseCount.value = 0
  latestRelease.value = null
  resetReleasePaging()
  config.value = null
  accessList.value = []
  activeTab.value = 'releases'
  if (repoId.value) loadRepositoryDetail()
})

watch(releaseSearch, resetFiltersAndReload)
watch([releaseSortKey, releaseDateRange], resetFiltersAndReload)
watch(releasePage, () => {
  if (repoId.value) loadRepositoryDetail()
})

onMounted(() => {
  if (repoId.value) loadRepositoryDetail()
})
</script>
