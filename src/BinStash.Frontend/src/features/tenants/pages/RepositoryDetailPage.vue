<template>
  <div class="space-y-6">
    <nav class="flex items-center gap-2 text-sm text-gray-500 dark:text-gray-400">
      <router-link :to="`/t/${tenantId}/repositories`" class="hover:text-violet-500 transition">Repositories</router-link>
      <IconChevronRight class="w-4 h-4 text-gray-300 dark:text-gray-600 shrink-0" />
      <span class="text-gray-700 dark:text-gray-200 font-medium truncate">{{ repo?.name ?? '…' }}</span>
    </nav>

    <div v-if="repoLoading" class="animate-pulse space-y-6">
      <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 p-6 space-y-4">
        <div class="h-3 w-32 rounded bg-gray-200 dark:bg-gray-700" />
        <div class="h-10 w-64 rounded bg-gray-200 dark:bg-gray-700" />
        <div class="h-16 rounded-xl bg-gray-200 dark:bg-gray-700" />
      </div>
      <div class="grid grid-cols-1 xl:grid-cols-3 gap-6">
        <div class="xl:col-span-2 h-96 rounded-2xl bg-gray-200 dark:bg-gray-700" />
        <div class="h-96 rounded-2xl bg-gray-200 dark:bg-gray-700" />
      </div>
    </div>

    <div v-else-if="repoError" class="rounded-2xl border border-rose-200 dark:border-rose-500/20 bg-rose-50 dark:bg-rose-500/10 px-5 py-4 text-sm text-rose-700 dark:text-rose-300">
      {{ repoError }}
    </div>

    <template v-else-if="repo">
      <section class="overflow-hidden rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs">
        <div class="h-1 bg-linear-to-r from-teal-400 via-violet-500 to-indigo-500" />
        <div class="p-5 lg:p-6">
          <div class="flex flex-col gap-5 lg:flex-row lg:items-start lg:justify-between">
            <div class="min-w-0 space-y-4">
              <div class="flex items-start gap-3">
                <div class="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-teal-100 text-teal-600 dark:bg-teal-500/20 dark:text-teal-300">
                  <IconGitBranch class="h-5.5 w-5.5" />
                </div>
                <div class="min-w-0">
                  <h1 class="truncate text-2xl font-bold text-gray-900 dark:text-white">{{ repo.name }}</h1>
                  <p class="mt-1.5 max-w-3xl text-sm leading-6 text-gray-500 dark:text-gray-400">
                    {{ repo.description || 'A release repository for ingesting and managing versioned package snapshots.' }}
                  </p>
                </div>
              </div>
            </div>
          </div>
        </div>
      </section>

      <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 p-1 shadow-xs">
        <nav class="flex flex-wrap gap-1">
          <button
            v-for="tab in tabs"
            :key="tab.key"
            type="button"
            class="inline-flex items-center gap-2 rounded-xl px-4 py-2.5 text-sm font-medium transition"
            :class="activeTab === tab.key
              ? 'bg-violet-500 text-white shadow-xs'
              : 'text-gray-500 hover:bg-gray-100 hover:text-gray-800 dark:text-gray-400 dark:hover:bg-gray-700/60 dark:hover:text-gray-100'"
            @click="activeTab = tab.key"
          >
            <component :is="tab.icon" class="h-4 w-4" />
            {{ tab.label }}
          </button>
        </nav>
      </div>

      <section v-if="activeTab === 'overview'" class="grid grid-cols-1 gap-6 xl:grid-cols-3">
        <div class="space-y-6 xl:col-span-2">
          <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
            <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
              <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Repository overview</h2>
              <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Core repository metadata and storage details in one place.</p>
            </div>
            <div class="grid grid-cols-1 gap-4 p-5 sm:grid-cols-2">
              <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4 sm:col-span-2">
                <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Description</div>
                <div class="mt-2 text-sm leading-6 text-gray-700 dark:text-gray-200">
                  {{ repo.description || 'A release repository for ingesting and managing versioned package snapshots.' }}
                </div>
              </div>
              <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Repository ID</div>
                <div class="mt-2 break-all font-mono text-xs text-gray-700 dark:text-gray-200">{{ repo.id }}</div>
              </div>
              <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Created</div>
                <div class="mt-2 text-sm font-medium text-gray-900 dark:text-white">{{ formatDateTime(repo.createdAt) }}</div>
              </div>
              <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Storage class</div>
                <div class="mt-2 text-sm font-medium text-gray-900 dark:text-white">{{ repo.storageClass }}</div>
              </div>
              <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4">
                <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Latest release</div>
                <div class="mt-2 text-sm font-medium text-gray-900 dark:text-white">{{ latestRelease?.version ?? 'No releases yet' }}</div>
                <div class="mt-1 text-xs text-gray-500 dark:text-gray-400">{{ latestRelease ? formatDateTime(latestRelease.createdAt) : 'Release activity will appear here once available.' }}</div>
              </div>
            </div>
          </div>

          <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
            <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
              <h3 class="font-semibold text-gray-900 dark:text-white">Chunking & deduplication summary</h3>
            </div>
            <div class="p-5">
              <div v-if="repo.chunker" class="flex flex-wrap gap-2">
                <span class="inline-flex items-center rounded-full border border-gray-200 dark:border-gray-700 px-2.5 py-1 text-xs font-medium text-gray-600 dark:text-gray-300">{{ repo.chunker.type }}</span>
                <span v-if="repo.chunker.avgChunkSize" class="inline-flex items-center rounded-full border border-gray-200 dark:border-gray-700 px-2.5 py-1 text-xs font-medium text-gray-600 dark:text-gray-300">Avg {{ formatBytes(repo.chunker.avgChunkSize) }}</span>
              </div>
              <p v-else class="text-sm text-gray-500 dark:text-gray-400">No chunker details are available for this repository.</p>
            </div>
          </div>
        </div>

        <div class="space-y-6">
          <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
            <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
              <h3 class="font-semibold text-gray-900 dark:text-white">Highlights</h3>
            </div>
            <div class="space-y-4 p-5">
              <div class="rounded-xl bg-gray-50 p-4 dark:bg-gray-900/30">
                <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Total releases</div>
                <div class="mt-1 text-lg font-bold text-gray-900 dark:text-white">{{ totalReleaseCount }}</div>
              </div>
              <div class="rounded-xl bg-gray-50 p-4 dark:bg-gray-900/30">
                <div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Last activity</div>
                <div class="mt-1 text-sm font-medium text-gray-900 dark:text-white">{{ latestRelease ? formatDateTime(latestRelease.createdAt) : '—' }}</div>
              </div>
            </div>
          </div>

          <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
            <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
              <h3 class="font-semibold text-gray-900 dark:text-white">Quick facts</h3>
            </div>
            <dl class="divide-y divide-gray-100 dark:divide-gray-700/60">
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Search mode</dt>
                <dd class="mt-1 text-sm font-medium text-gray-800 dark:text-gray-100">GraphQL filtered query</dd>
              </div>
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Visible on current page</dt>
                <dd class="mt-1 text-sm font-medium text-gray-800 dark:text-gray-100">{{ releases.length }} releases</dd>
              </div>
            </dl>
          </div>
        </div>
      </section>

      <section v-if="activeTab === 'releases'" class="grid grid-cols-1 xl:grid-cols-3 gap-6">
        <div class="xl:col-span-3 space-y-6">
          <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
            <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
              <div class="flex flex-col gap-4 xl:flex-row xl:items-center xl:justify-between">
                <div>
                  <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Release activity</h2>
                  <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
                    Showing {{ releases.length }} of {{ filteredReleaseCount }} matching releases across {{ totalReleaseCount }} total.
                  </p>
                </div>
                <div class="grid gap-3 sm:grid-cols-3 xl:min-w-2xl">
                  <SearchForm v-model="releaseSearch" placeholder="Search by version or notes…" />
                  <select v-model="releaseDateRange" class="form-select w-full">
                    <option value="all">All time</option>
                    <option value="30d">Last 30 days</option>
                    <option value="90d">Last 90 days</option>
                  </select>
                  <select v-model="releaseSortKey" class="form-select w-full">
                    <option value="createdAt:DESC">Newest first</option>
                    <option value="createdAt:ASC">Oldest first</option>
                    <option value="version:ASC">Version A → Z</option>
                    <option value="version:DESC">Version Z → A</option>
                  </select>
                </div>
              </div>
            </div>

            <div v-if="releasesLoading" class="flex items-center justify-center py-16">
              <div class="h-8 w-8 animate-spin rounded-full border-2 border-violet-500/20 border-t-violet-500" />
            </div>

            <div v-else-if="releases.length === 0" class="px-6 py-16 text-center">
              <div class="mx-auto mb-4 flex h-14 w-14 items-center justify-center rounded-2xl bg-gray-100 text-gray-400 dark:bg-gray-700/50 dark:text-gray-500">
                <IconPackage class="h-7 w-7" />
              </div>
              <h3 class="text-sm font-semibold text-gray-700 dark:text-gray-200">
                {{ releaseSearch || releaseDateRange !== 'all' ? 'No matching releases' : 'No releases yet' }}
              </h3>
              <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">
                {{ releaseSearch || releaseDateRange !== 'all' ? 'Try adjusting the filters to broaden your search.' : 'New ingested releases will appear here automatically.' }}
              </p>
            </div>

            <div v-else class="divide-y divide-gray-100 dark:divide-gray-700/60">
              <article v-for="rel in releases" :key="rel.id" class="group px-5 py-4 transition hover:bg-gray-50 dark:hover:bg-gray-700/20">
                <div class="flex flex-col gap-4 lg:flex-row lg:items-center lg:justify-between">
                  <div class="min-w-0 flex items-start gap-3">
                    <div class="mt-0.5 flex h-10 w-10 shrink-0 items-center justify-center rounded-xl bg-emerald-100 text-emerald-600 dark:bg-emerald-500/20 dark:text-emerald-300">
                      <IconTag class="h-4.5 w-4.5" />
                    </div>
                    <div class="min-w-0">
                      <div class="flex flex-wrap items-center gap-2">
                        <router-link :to="releaseLink(rel.id)" class="text-sm font-semibold text-gray-900 transition group-hover:text-violet-600 dark:text-white dark:group-hover:text-violet-400">
                          {{ rel.version }}
                        </router-link>
                        <span class="inline-flex items-center rounded-full bg-gray-100 px-2 py-0.5 text-[11px] font-medium text-gray-500 dark:bg-gray-700/60 dark:text-gray-300">
                          {{ formatDate(rel.createdAt) }}
                        </span>
                        <span class="inline-flex items-center gap-1 rounded-full px-2 py-0.5 text-[11px] font-semibold"
                          :class="scoreBadgeClasses(getReleaseScore(rel))">
                          Score {{ formatReleaseScore(getReleaseScore(rel)) }}
                        </span>
                      </div>
                      <p class="mt-1 line-clamp-2 text-sm text-gray-500 dark:text-gray-400">
                        {{ rel.notes || 'No release notes were provided for this version.' }}
                      </p>
                    </div>
                  </div>
                  <div class="flex items-center gap-2 lg:justify-end">
                    <router-link :to="releaseLink(rel.id)" class="btn border-gray-200 hover:border-gray-300 dark:border-gray-700/60 dark:hover:border-gray-600">
                      Details
                    </router-link>
                    <a :href="releaseDownloadUrl(rel.id)" target="_blank" class="btn bg-violet-500 text-white hover:bg-violet-600">
                      <span class="inline-flex items-center gap-2"><IconDownload class="h-4 w-4" /> Download</span>
                    </a>
                  </div>
                </div>
              </article>
            </div>

            <div class="flex flex-col gap-3 border-t border-gray-200 dark:border-gray-700/60 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
              <div class="text-xs text-gray-500 dark:text-gray-400">Page {{ releasePage }} · {{ filteredReleaseCount }} matching release<span v-if="filteredReleaseCount !== 1">s</span></div>
              <div class="flex items-center gap-2">
                <button type="button" class="btn border-gray-200 hover:border-gray-300 dark:border-gray-700/60 dark:hover:border-gray-600" :disabled="!releaseHasPreviousPage || releasesLoading" @click="goToPreviousReleasePage">Previous</button>
                <button type="button" class="btn border-gray-200 hover:border-gray-300 dark:border-gray-700/60 dark:hover:border-gray-600" :disabled="!releaseHasNextPage || releasesLoading" @click="goToNextReleasePage">Next</button>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section v-if="activeTab === 'config'" class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
        <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4 flex items-center gap-2">
          <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-amber-100 text-amber-600 dark:bg-amber-500/20 dark:text-amber-300">
            <IconAdjustmentsHorizontal class="h-4 w-4" />
          </div>
          <h3 class="font-semibold text-gray-900 dark:text-white">Deduplication configuration</h3>
        </div>
        <div v-if="configLoading" class="flex items-center justify-center py-16"><div class="h-8 w-8 animate-spin rounded-full border-2 border-violet-500/20 border-t-violet-500" /></div>
        <div v-else-if="configError" class="p-5"><div class="rounded-xl border border-rose-200 dark:border-rose-500/20 bg-rose-50 dark:bg-rose-500/10 px-4 py-3 text-sm text-rose-700 dark:text-rose-300">{{ configError }}</div></div>
        <div v-else-if="config" class="grid grid-cols-1 gap-4 p-5 sm:grid-cols-2 xl:grid-cols-3">
          <div class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4"><div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Chunker</div><div class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ config.dedupeConfig.chunker }}</div></div>
          <div v-if="config.dedupeConfig.minChunkSize != null" class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4"><div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Min chunk size</div><div class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ formatBytes(config.dedupeConfig.minChunkSize) }}</div></div>
          <div v-if="config.dedupeConfig.avgChunkSize != null" class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4"><div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Average chunk size</div><div class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ formatBytes(config.dedupeConfig.avgChunkSize) }}</div></div>
          <div v-if="config.dedupeConfig.maxChunkSize != null" class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4"><div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Max chunk size</div><div class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ formatBytes(config.dedupeConfig.maxChunkSize) }}</div></div>
          <div v-if="config.dedupeConfig.shiftCount != null" class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4"><div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Shift count</div><div class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ config.dedupeConfig.shiftCount }}</div></div>
          <div v-if="config.dedupeConfig.boundaryCheckBytes != null" class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4"><div class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Boundary check</div><div class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ formatBytes(config.dedupeConfig.boundaryCheckBytes) }}</div></div>
        </div>
      </section>

      <section v-if="activeTab === 'access'" class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs overflow-hidden">
        <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4 flex items-center gap-2">
          <div class="flex h-8 w-8 items-center justify-center rounded-lg bg-violet-100 text-violet-600 dark:bg-violet-500/20 dark:text-violet-300">
            <IconLock class="h-4 w-4" />
          </div>
          <h3 class="font-semibold text-gray-900 dark:text-white">Access control</h3>
        </div>
        <div v-if="accessLoading" class="flex items-center justify-center py-16"><div class="h-8 w-8 animate-spin rounded-full border-2 border-violet-500/20 border-t-violet-500" /></div>
        <div v-else-if="accessError" class="p-5"><div class="rounded-xl border border-rose-200 dark:border-rose-500/20 bg-rose-50 dark:bg-rose-500/10 px-4 py-3 text-sm text-rose-700 dark:text-rose-300">{{ accessError }}</div></div>
        <div v-else-if="accessList.length === 0" class="px-6 py-16 text-center"><IconLock class="mx-auto mb-3 h-10 w-10 text-gray-300 dark:text-gray-600" /><p class="text-sm font-medium text-gray-600 dark:text-gray-300">No access entries configured.</p></div>
        <div v-else class="divide-y divide-gray-100 dark:divide-gray-700/60">
          <div v-for="entry in accessList" :key="`${entry.subjectType}-${entry.subjectId}`" class="flex flex-col gap-3 px-5 py-4 sm:flex-row sm:items-center sm:justify-between">
            <div class="flex items-center gap-3 min-w-0">
              <div class="flex h-10 w-10 items-center justify-center rounded-xl bg-sky-100 text-sky-600 dark:bg-sky-500/20 dark:text-sky-300">
                <component :is="entry.subjectType === 0 ? IconUser : IconRobot" class="h-4.5 w-4.5" />
              </div>
              <div class="min-w-0">
                <div class="truncate text-sm font-semibold text-gray-900 dark:text-white">{{ entry.subjectType === 0 ? 'User principal' : 'Service account' }}</div>
                <div class="truncate font-mono text-xs text-gray-500 dark:text-gray-400">{{ entry.subjectId }}</div>
              </div>
            </div>
            <div class="flex items-center gap-3 sm:justify-end">
              <span class="inline-flex items-center rounded-full bg-violet-100 px-2.5 py-1 text-xs font-semibold text-violet-700 dark:bg-violet-500/20 dark:text-violet-300">{{ entry.role }}</span>
              <span class="text-xs text-gray-500 dark:text-gray-400">{{ formatDate(entry.grantedAt) }}</span>
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
import SearchForm from '@/components/SearchForm.vue'
import {
  IconAdjustmentsHorizontal,
  IconCalendar,
  IconChevronRight,
  IconDatabase,
  IconDownload,
  IconGitBranch,
  IconLock,
  IconPackage,
  IconRobot,
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