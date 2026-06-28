<template>
  <div class="font-montserrat space-y-6">
    <nav class="flex items-center gap-2 text-sm text-ink-muted">
      <router-link :to="`/t/${tenantId}/repositories`" class="transition hover:text-accent">Repositories</router-link>
      <IconChevronRight class="w-4 h-4 text-ink-subtle shrink-0" />
      <span class="font-medium text-ink-strong truncate">{{ repo?.name ?? '…' }}</span>
    </nav>

    <div v-if="repoLoading" class="animate-pulse space-y-6">
      <div class="rounded-card border border-hairline bg-card p-6 space-y-4">
        <div class="h-3 w-32 rounded bg-hairline" />
        <div class="h-10 w-64 rounded bg-hairline" />
        <div class="h-16 rounded-xl bg-hairline" />
      </div>
      <div class="grid grid-cols-1 xl:grid-cols-3 gap-6">
        <div class="xl:col-span-2 h-96 rounded-card bg-hairline" />
        <div class="h-96 rounded-card bg-hairline" />
      </div>
    </div>

    <div v-else-if="repoError" class="rounded-card border border-danger/20 bg-danger-soft px-5 py-4 text-sm text-danger">
      {{ repoError }}
    </div>

    <template v-else-if="repo">
      <section class="overflow-hidden rounded-card border border-hairline bg-card">
        <div class="h-1 bg-linear-to-r from-success via-accent to-brand-to" />
        <div class="p-5 lg:p-6">
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
        </div>
      </section>

      <div class="rounded-card border border-hairline bg-card p-1.5">
        <nav class="flex flex-wrap gap-1">
          <button
            v-for="tab in tabs"
            :key="tab.key"
            type="button"
            class="inline-flex items-center gap-2 rounded-full px-4 py-2 text-sm font-medium transition"
            :class="activeTab === tab.key
              ? 'bg-accent text-white'
              : 'text-ink-muted hover:text-ink-strong'"
            @click="activeTab = tab.key"
          >
            <component :is="tab.icon" class="h-4 w-4" />
            {{ tab.label }}
          </button>
        </nav>
      </div>

      <section v-if="activeTab === 'overview'" class="grid grid-cols-1 gap-6 xl:grid-cols-3">
        <div class="space-y-6 xl:col-span-2">
          <div class="rounded-card border border-hairline bg-card overflow-hidden">
            <div class="border-b border-hairline px-5 py-4">
              <h2 class="text-lg font-semibold text-ink-strong">Repository overview</h2>
              <p class="mt-1 text-sm text-ink-muted">Core repository metadata and storage details in one place.</p>
            </div>
            <div class="grid grid-cols-1 gap-4 p-5 sm:grid-cols-2">
              <div class="rounded-2xl border border-hairline p-4 sm:col-span-2">
                <div class="text-xs uppercase tracking-wide text-ink-subtle">Description</div>
                <div class="mt-2 text-sm leading-6 text-ink">
                  {{ repo.description || 'A release repository for ingesting and managing versioned package snapshots.' }}
                </div>
              </div>
              <div class="rounded-2xl border border-hairline p-4">
                <div class="text-xs uppercase tracking-wide text-ink-subtle">Repository ID</div>
                <div class="mt-2 break-all font-mono text-xs text-ink">{{ repo.id }}</div>
              </div>
              <div class="rounded-2xl border border-hairline p-4">
                <div class="text-xs uppercase tracking-wide text-ink-subtle">Created</div>
                <div class="mt-2 text-sm font-medium text-ink-strong">{{ formatDateTime(repo.createdAt) }}</div>
              </div>
              <div class="rounded-2xl border border-hairline p-4">
                <div class="text-xs uppercase tracking-wide text-ink-subtle">Storage class</div>
                <div class="mt-2 text-sm font-medium text-ink-strong">{{ repo.storageClass }}</div>
              </div>
              <div class="rounded-2xl border border-hairline p-4">
                <div class="text-xs uppercase tracking-wide text-ink-subtle">Latest release</div>
                <div class="mt-2 text-sm font-medium text-ink-strong">{{ latestRelease?.version ?? 'No releases yet' }}</div>
                <div class="mt-1 text-xs text-ink-muted">{{ latestRelease ? formatDateTime(latestRelease.createdAt) : 'Release activity will appear here once available.' }}</div>
              </div>
            </div>
          </div>

          <div class="rounded-card border border-hairline bg-card overflow-hidden">
            <div class="border-b border-hairline px-5 py-4">
              <h3 class="font-semibold text-ink-strong">Chunking &amp; deduplication summary</h3>
            </div>
            <div class="p-5">
              <div v-if="repo.chunker" class="flex flex-wrap gap-2">
                <span class="inline-flex items-center rounded-full border border-hairline px-2.5 py-1 text-xs font-medium text-ink-muted">{{ repo.chunker.type }}</span>
                <span v-if="repo.chunker.avgChunkSize" class="inline-flex items-center rounded-full border border-hairline px-2.5 py-1 text-xs font-medium text-ink-muted">Avg {{ formatBytes(repo.chunker.avgChunkSize) }}</span>
              </div>
              <p v-else class="text-sm text-ink-muted">No chunker details are available for this repository.</p>
            </div>
          </div>
        </div>

        <div class="space-y-6">
          <div class="rounded-card border border-hairline bg-card overflow-hidden">
            <div class="border-b border-hairline px-5 py-4">
              <h3 class="font-semibold text-ink-strong">Highlights</h3>
            </div>
            <div class="space-y-4 p-5">
              <div class="rounded-2xl border border-hairline p-4">
                <div class="text-xs uppercase tracking-wide text-ink-subtle">Total releases</div>
                <div class="mt-1 text-lg font-bold text-ink-strong">{{ totalReleaseCount }}</div>
              </div>
              <div class="rounded-2xl border border-hairline p-4">
                <div class="text-xs uppercase tracking-wide text-ink-subtle">Last activity</div>
                <div class="mt-1 text-sm font-medium text-ink-strong">{{ latestRelease ? formatDateTime(latestRelease.createdAt) : '—' }}</div>
              </div>
            </div>
          </div>

          <div class="rounded-card border border-hairline bg-card overflow-hidden">
            <div class="border-b border-hairline px-5 py-4">
              <h3 class="font-semibold text-ink-strong">Quick facts</h3>
            </div>
            <dl class="divide-y divide-hairline">
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-ink-subtle">Search mode</dt>
                <dd class="mt-1 text-sm font-medium text-ink-strong">GraphQL filtered query</dd>
              </div>
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-ink-subtle">Visible on current page</dt>
                <dd class="mt-1 text-sm font-medium text-ink-strong">{{ releases.length }} releases</dd>
              </div>
            </dl>
          </div>
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
                <div class="relative">
                  <IconSearch class="pointer-events-none absolute left-3 top-1/2 size-4 -translate-y-1/2 text-ink-muted" />
                  <input
                    v-model="releaseSearch"
                    type="search"
                    placeholder="Search by version or notes…"
                    class="h-10 w-full rounded-full border border-hairline bg-transparent pl-9 pr-3 text-sm text-ink-strong outline-none transition placeholder:text-ink-muted focus:border-accent"
                  />
                </div>
                <select v-model="releaseDateRange" class="h-10 w-full rounded-full border border-hairline bg-transparent px-3 text-sm text-ink-strong outline-none transition focus:border-accent">
                  <option value="all">All time</option>
                  <option value="30d">Last 30 days</option>
                  <option value="90d">Last 90 days</option>
                </select>
                <select v-model="releaseSortKey" class="h-10 w-full rounded-full border border-hairline bg-transparent px-3 text-sm text-ink-strong outline-none transition focus:border-accent">
                  <option value="createdAt:DESC">Newest first</option>
                  <option value="createdAt:ASC">Oldest first</option>
                  <option value="version:ASC">Version A → Z</option>
                  <option value="version:DESC">Version Z → A</option>
                </select>
              </div>
            </div>
          </div>

          <div v-if="releasesLoading" class="flex items-center justify-center py-16">
            <div class="h-8 w-8 animate-spin rounded-full border-2 border-accent/20 border-t-accent" />
          </div>

          <div v-else-if="releases.length === 0" class="px-6 py-16 text-center">
            <div class="mx-auto mb-4 flex size-14 items-center justify-center rounded-2xl bg-accent-soft text-accent">
              <IconPackage class="h-7 w-7" />
            </div>
            <h3 class="text-sm font-semibold text-ink-strong">
              {{ releaseSearch || releaseDateRange !== 'all' ? 'No matching releases' : 'No releases yet' }}
            </h3>
            <p class="mt-1 text-sm text-ink-muted">
              {{ releaseSearch || releaseDateRange !== 'all' ? 'Try adjusting the filters to broaden your search.' : 'New ingested releases will appear here automatically.' }}
            </p>
          </div>

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
                  <router-link :to="releaseLink(rel.id)" class="rounded-full border border-hairline px-3.5 py-2 text-sm font-medium text-ink-muted transition hover:text-ink-strong">
                    Details
                  </router-link>
                  <a :href="releaseDownloadUrl(rel.id)" target="_blank" class="inline-flex items-center gap-2 rounded-full bg-accent px-3.5 py-2 text-sm font-medium text-white transition hover:brightness-110">
                    <IconDownload class="h-4 w-4" /> Download
                  </a>
                </div>
              </div>
            </article>
          </div>

          <div class="flex flex-col gap-3 border-t border-hairline px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
            <div class="text-xs text-ink-muted">Page {{ releasePage }} · {{ filteredReleaseCount }} matching release<span v-if="filteredReleaseCount !== 1">s</span></div>
            <div class="flex items-center gap-2">
              <button type="button" class="rounded-full border border-hairline px-3.5 py-2 text-sm font-medium text-ink-muted transition hover:text-ink-strong disabled:opacity-40" :disabled="!releaseHasPreviousPage || releasesLoading" @click="goToPreviousReleasePage">Previous</button>
              <button type="button" class="rounded-full border border-hairline px-3.5 py-2 text-sm font-medium text-ink-muted transition hover:text-ink-strong disabled:opacity-40" :disabled="!releaseHasNextPage || releasesLoading" @click="goToNextReleasePage">Next</button>
            </div>
          </div>
        </div>
      </section>

      <section v-if="activeTab === 'config'" class="rounded-card border border-hairline bg-card overflow-hidden">
        <div class="border-b border-hairline px-5 py-4 flex items-center gap-3">
          <div class="flex size-9 items-center justify-center rounded-full bg-accent-soft text-accent">
            <IconAdjustmentsHorizontal class="h-4 w-4" />
          </div>
          <h3 class="font-semibold text-ink-strong">Deduplication configuration</h3>
        </div>
        <div v-if="configLoading" class="flex items-center justify-center py-16"><div class="h-8 w-8 animate-spin rounded-full border-2 border-accent/20 border-t-accent" /></div>
        <div v-else-if="configError" class="p-5"><div class="rounded-2xl border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">{{ configError }}</div></div>
        <div v-else-if="config" class="grid grid-cols-1 gap-4 p-5 sm:grid-cols-2 xl:grid-cols-3">
          <div class="rounded-2xl border border-hairline p-4"><div class="text-xs uppercase tracking-wide text-ink-subtle">Chunker</div><div class="mt-1 text-sm font-semibold text-ink-strong">{{ config.dedupeConfig.chunker }}</div></div>
          <div v-if="config.dedupeConfig.minChunkSize != null" class="rounded-2xl border border-hairline p-4"><div class="text-xs uppercase tracking-wide text-ink-subtle">Min chunk size</div><div class="mt-1 text-sm font-semibold text-ink-strong">{{ formatBytes(config.dedupeConfig.minChunkSize) }}</div></div>
          <div v-if="config.dedupeConfig.avgChunkSize != null" class="rounded-2xl border border-hairline p-4"><div class="text-xs uppercase tracking-wide text-ink-subtle">Average chunk size</div><div class="mt-1 text-sm font-semibold text-ink-strong">{{ formatBytes(config.dedupeConfig.avgChunkSize) }}</div></div>
          <div v-if="config.dedupeConfig.maxChunkSize != null" class="rounded-2xl border border-hairline p-4"><div class="text-xs uppercase tracking-wide text-ink-subtle">Max chunk size</div><div class="mt-1 text-sm font-semibold text-ink-strong">{{ formatBytes(config.dedupeConfig.maxChunkSize) }}</div></div>
          <div v-if="config.dedupeConfig.shiftCount != null" class="rounded-2xl border border-hairline p-4"><div class="text-xs uppercase tracking-wide text-ink-subtle">Shift count</div><div class="mt-1 text-sm font-semibold text-ink-strong">{{ config.dedupeConfig.shiftCount }}</div></div>
          <div v-if="config.dedupeConfig.boundaryCheckBytes != null" class="rounded-2xl border border-hairline p-4"><div class="text-xs uppercase tracking-wide text-ink-subtle">Boundary check</div><div class="mt-1 text-sm font-semibold text-ink-strong">{{ formatBytes(config.dedupeConfig.boundaryCheckBytes) }}</div></div>
        </div>
      </section>

      <section v-if="activeTab === 'access'" class="rounded-card border border-hairline bg-card overflow-hidden">
        <div class="border-b border-hairline px-5 py-4 flex items-center gap-3">
          <div class="flex size-9 items-center justify-center rounded-full bg-accent-soft text-accent">
            <IconLock class="h-4 w-4" />
          </div>
          <h3 class="font-semibold text-ink-strong">Access control</h3>
        </div>
        <div v-if="accessLoading" class="flex items-center justify-center py-16"><div class="h-8 w-8 animate-spin rounded-full border-2 border-accent/20 border-t-accent" /></div>
        <div v-else-if="accessError" class="p-5"><div class="rounded-2xl border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">{{ accessError }}</div></div>
        <div v-else-if="accessList.length === 0" class="px-6 py-16 text-center"><IconLock class="mx-auto mb-3 h-10 w-10 text-ink-subtle" /><p class="text-sm font-medium text-ink-muted">No access entries configured.</p></div>
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
              <span class="inline-flex items-center rounded-full bg-accent-soft px-2.5 py-1 text-xs font-semibold text-accent">{{ entry.role }}</span>
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
import {
  IconAdjustmentsHorizontal,
  IconChevronRight,
  IconDatabase,
  IconDownload,
  IconGitBranch,
  IconLock,
  IconPackage,
  IconRobot,
  IconSearch,
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
    { key: 'overview', label: 'Overview', icon: IconDatabase },
    { key: 'releases', label: 'Releases', icon: IconPackage },
    { key: 'config', label: 'Configuration', icon: IconAdjustmentsHorizontal },
  ]
  if (isTenantAdmin.value) base.push({ key: 'access', label: 'Access Control', icon: IconLock })
  return base
})

const activeTab = ref('overview')
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

watch(activeTab, (tab) => {
  if (tab === 'config') loadConfig()
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
  activeTab.value = 'overview'
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