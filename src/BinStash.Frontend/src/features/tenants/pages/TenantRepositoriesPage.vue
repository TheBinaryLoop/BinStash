<template>
  <div class="font-montserrat">
  <!-- Page header -->
  <div class="mb-8 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
    <div>
      <h1 class="text-[1.75rem] font-bold leading-8 tracking-tight text-ink-strong">Repositories</h1>
      <p class="mt-1 text-sm text-ink-muted">Browse and manage repositories for this tenant.</p>
    </div>
    <div v-if="isTenantAdmin" class="flex items-center gap-2">
      <button
        class="inline-flex h-9 items-center gap-1.5 rounded-full bg-accent px-4 text-sm font-medium text-white shadow-sm transition hover:brightness-110"
        @click="openCreate"
      >
        <IconPlus class="h-4 w-4 shrink-0" />
        <span>New Repository</span>
      </button>
    </div>
  </div>

  <!-- KPI cards -->
  <div class="mb-6 grid gap-3.5 md:grid-cols-3">
    <div class="flex h-25 items-center gap-5 rounded-card border border-hairline bg-raised px-4">
      <div class="flex size-12 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent"><IconGitBranch class="size-6" /></div>
      <div>
        <div class="text-[2rem] font-bold leading-8 text-ink-strong">{{ filteredItems.length }}</div>
        <div class="text-sm text-ink-muted">Total Repositories</div>
      </div>
    </div>
    <div class="flex h-25 items-center gap-5 rounded-card border border-hairline bg-raised px-4">
      <div class="flex size-12 shrink-0 items-center justify-center rounded-full bg-success-soft text-success"><IconPackage class="size-6" /></div>
      <div>
        <div class="text-[2rem] font-bold leading-8 text-ink-strong">{{ totalReleaseCount }}</div>
        <div class="text-sm text-ink-muted">Total Releases</div>
      </div>
    </div>
    <div class="flex h-25 items-center gap-5 rounded-card border border-hairline bg-raised px-4">
      <div class="flex size-12 shrink-0 items-center justify-center rounded-full bg-warning-soft text-warning"><IconStar class="size-6" /></div>
      <div>
        <div class="text-[2rem] font-bold leading-8 text-ink-strong">{{ starredRepoIds.size }}</div>
        <div class="text-sm text-ink-muted">Starred Repositories</div>
      </div>
    </div>
  </div>

  <!-- Search / filters / view mode -->
  <div class="mb-5 flex flex-col gap-3 sm:flex-row sm:items-center">
    <div class="relative flex-1">
      <IconSearch class="pointer-events-none absolute left-3 top-1/2 size-5 -translate-y-1/2 text-ink-muted" />
      <input
        v-model="searchQuery"
        type="search"
        placeholder="Search repositories..."
        class="h-[42px] w-full rounded-full border border-hairline bg-transparent pl-10 pr-4 text-sm text-ink-strong outline-none transition placeholder:text-ink-muted focus:border-accent"
      />
    </div>
    <button
      type="button"
      class="inline-flex h-[42px] items-center justify-center gap-2 rounded-full border border-hairline px-4 text-sm font-medium text-ink-muted transition hover:text-ink-strong"
      @click="filtersOpen = true"
    >
      <IconFilter class="size-4" />
      <span>Filters</span>
    </button>
    <div class="flex h-[42px] items-center gap-1 self-start rounded-full border border-hairline px-1">
      <button
        type="button"
        class="flex size-8 items-center justify-center rounded-full transition"
        :class="viewMode === 'grid' ? 'bg-accent-soft text-accent' : 'text-ink-muted hover:text-ink-strong'"
        aria-label="Grid view"
        @click="viewMode = 'grid'"
      >
        <IconLayoutGrid class="size-4" />
      </button>
      <button
        type="button"
        class="flex size-8 items-center justify-center rounded-full transition"
        :class="viewMode === 'list' ? 'bg-accent-soft text-accent' : 'text-ink-muted hover:text-ink-strong'"
        aria-label="List view"
        @click="viewMode = 'list'"
      >
        <IconListDetails class="size-4" />
      </button>
    </div>
  </div>

  <!-- Loading -->
  <div v-if="isLoading" class="flex items-center justify-center py-20">
    <svg class="animate-spin w-8 h-8 text-accent" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
      <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
      <path class="opacity-75 fill-current" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
    </svg>
  </div>

  <!-- Error -->
  <div v-else-if="error" class="rounded-card bg-danger-soft px-5 py-4 text-sm text-danger">
    {{ error }}
  </div>

  <!-- Empty -->
  <div v-else-if="filteredItems.length === 0" class="py-20 text-center">
    <div class="mx-auto mb-4 flex size-16 items-center justify-center rounded-2xl bg-accent-soft">
      <IconGitBranch class="size-8 text-accent" />
    </div>
    <div class="mb-1 text-lg font-semibold text-ink-strong">
      {{ searchQuery ? 'No results found' : 'No repositories yet' }}
    </div>
    <div class="mb-5 text-sm text-ink-muted">
      {{ searchQuery
        ? 'Try adjusting your search term.'
        : 'Create your first repository to start storing releases.' }}
    </div>
    <button
      v-if="!searchQuery && isTenantAdmin"
      class="inline-flex items-center gap-2 rounded-full bg-accent px-4 py-2.5 text-sm font-medium text-white shadow-sm transition hover:brightness-110"
      @click="openCreate"
    >
      <IconPlus class="h-4 w-4 shrink-0" />
      Create Repository
    </button>
  </div>

  <!-- Repository grid -->
  <div v-else-if="viewMode === 'grid'" class="grid gap-5 sm:grid-cols-2 xl:grid-cols-3">
    <router-link
      v-for="repo in filteredItems"
      :key="repo.id"
      :to="`/t/${tenantId}/repositories/${repo.id}`"
      class="group flex flex-col overflow-hidden rounded-card border border-hairline bg-card transition hover:border-accent/50"
    >
      <!-- Header: icon + name + star + storage class + description -->
      <div class="flex flex-col gap-3 border-b border-hairline p-5">
        <div class="flex items-start gap-3">
          <div class="flex size-12 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent">
            <IconGitBranch class="size-6" />
          </div>
          <div class="min-w-0 flex-1">
            <div class="flex items-center justify-between gap-2">
              <span class="truncate text-base font-semibold text-ink-strong transition group-hover:text-accent">{{ repo.name }}</span>
              <button
                type="button"
                class="shrink-0 transition hover:scale-110"
                @click="toggleStar(repo.id, $event)"
              >
                <IconStarFilled v-if="starredRepoIds.has(repo.id)" class="size-5 text-warning" />
                <IconStar v-else class="size-5 text-ink-subtle hover:text-warning" />
              </button>
            </div>
            <span class="mt-1.5 inline-flex items-center rounded-full bg-hairline px-2 py-0.5 text-xs text-ink-muted">
              {{ repo.storageClass }}
            </span>
          </div>
        </div>
        <p class="line-clamp-1 text-sm text-ink-muted">
          {{ repo.description || 'No description provided.' }}
        </p>
      </div>

      <!-- Meta: release count -->
      <div class="flex items-center gap-3 px-5 py-3">
        <span class="inline-flex items-center gap-2 text-sm">
          <IconPackage class="size-4 text-ink-muted" />
          <span class="font-medium text-ink-strong">{{ releaseCountMap[repo.id] ?? 0 }}</span>
          <span class="text-ink-muted">Releases</span>
        </span>
      </div>

      <!-- Footer: updated + open -->
      <div class="mt-auto flex items-center justify-between border-t border-hairline px-5 py-4">
        <span class="inline-flex items-center gap-2 text-xs text-ink-muted">
          <IconClock class="size-3.5 shrink-0" />
          Updated recently
        </span>
        <span class="inline-flex items-center gap-1 text-sm font-medium text-accent transition group-hover:gap-1.5">
          Open <IconArrowRight class="size-4" />
        </span>
      </div>
    </router-link>
  </div>

  <!-- Repository list -->
  <div v-else class="overflow-hidden rounded-card border border-hairline bg-card">
    <div class="overflow-x-auto">
      <table class="min-w-full divide-y divide-hairline">
        <thead class="bg-raised">
          <tr>
            <th class="px-5 py-4 text-left text-xs font-semibold uppercase tracking-wide text-ink-subtle">Repository</th>
            <th class="px-5 py-4 text-left text-xs font-semibold uppercase tracking-wide text-ink-subtle">Storage</th>
            <th class="px-5 py-4 text-left text-xs font-semibold uppercase tracking-wide text-ink-subtle">Description</th>
            <th class="px-5 py-4 text-left text-xs font-semibold uppercase tracking-wide text-ink-subtle">Releases</th>
            <th class="px-5 py-4 text-left text-xs font-semibold uppercase tracking-wide text-ink-subtle">Last Updated</th>
            <th class="px-5 py-4"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-hairline">
          <tr v-for="repo in filteredItems" :key="repo.id" class="transition hover:bg-raised">
            <td class="px-5 py-4">
              <router-link :to="`/t/${tenantId}/repositories/${repo.id}`" class="flex items-center gap-3">
                <button
                  type="button"
                  class="shrink-0 transition hover:scale-110"
                  @click.prevent="toggleStar(repo.id, $event)"
                >
                  <IconStarFilled v-if="starredRepoIds.has(repo.id)" class="size-4 text-warning" />
                  <IconStar v-else class="size-4 text-ink-subtle" />
                </button>
                <div class="flex size-10 items-center justify-center rounded-full bg-accent-soft text-accent"><IconGitBranch class="size-5" /></div>
                <div>
                  <div class="text-sm font-semibold text-ink-strong">{{ repo.name }}</div>
                </div>
              </router-link>
            </td>
            <td class="px-5 py-4">
              <span class="inline-flex items-center rounded-full bg-hairline px-2.5 py-0.5 text-xs text-ink-muted">
                {{ repo.storageClass }}
              </span>
            </td>
            <td class="px-5 py-4 text-sm text-ink-muted">{{ repo.description || 'No description provided.' }}</td>
            <td class="px-5 py-4">
              <div class="flex items-center gap-1.5 text-sm text-ink-strong">
                <IconPackage class="size-3.5 text-ink-muted" />
                {{ releaseCountMap[repo.id] ?? 0 }}
              </div>
            </td>
            <td class="px-5 py-4">
              <div class="flex items-center gap-1.5 text-sm text-ink-muted">
                <IconClock class="size-3.5" />
                Recently
              </div>
            </td>
            <td class="px-5 py-4 text-right">
              <router-link :to="`/t/${tenantId}/repositories/${repo.id}`" class="inline-flex items-center gap-1 text-sm font-medium text-accent">
                Open <IconArrowRight class="size-4" />
              </router-link>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>

  </div>

  <!-- Filters / create modals -->
  <Teleport to="body">
    <div v-if="filtersOpen" class="fixed inset-0 z-50 font-montserrat">
      <div class="absolute inset-0 bg-slate-950/50 backdrop-blur-sm" @click="filtersOpen = false" />
      <div class="absolute inset-0 flex items-center justify-center px-4 py-6">
        <div class="w-full max-w-2xl rounded-card border border-hairline bg-panel shadow-2xl">
          <div class="flex items-center justify-between border-b border-hairline px-6 py-5">
            <div>
              <h3 class="text-lg font-semibold text-ink-strong">Filter Repositories</h3>
              <p class="mt-1 text-sm text-ink-muted">Refine your repository search with advanced filters</p>
            </div>
            <button class="text-ink-muted transition hover:text-ink-strong" @click="filtersOpen = false">
              <IconX class="h-5 w-5" />
            </button>
          </div>
          <div class="space-y-6 px-6 py-6">
            <!-- Storage class pills -->
            <div>
              <label class="mb-3 block text-sm font-medium text-ink-strong">Storage Class</label>
              <div class="flex flex-wrap gap-2">
                <button
                  type="button"
                  class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
                  :class="storageFilter === 'all' ? 'bg-accent text-white' : 'bg-hairline text-ink-muted hover:text-ink-strong'"
                  @click="storageFilter = 'all'"
                >All</button>
                <button
                  v-for="storage in storageClassOptions"
                  :key="storage"
                  type="button"
                  class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
                  :class="storageFilter === storage ? 'bg-accent text-white' : 'bg-hairline text-ink-muted hover:text-ink-strong'"
                  @click="storageFilter = storage"
                >{{ storage }}</button>
              </div>
            </div>

            <!-- Releases pills -->
            <div>
              <label class="mb-3 block text-sm font-medium text-ink-strong">Releases</label>
              <div class="flex flex-wrap gap-2">
                <button
                  v-for="opt in [{ value: 'all', label: 'All' }, { value: 'with', label: 'With Releases' }, { value: 'without', label: 'Without Releases' }]"
                  :key="opt.value"
                  type="button"
                  class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
                  :class="releasePresenceFilter === opt.value ? 'bg-accent text-white' : 'bg-hairline text-ink-muted hover:text-ink-strong'"
                  @click="releasePresenceFilter = opt.value"
                >{{ opt.label }}</button>
              </div>
            </div>

            <!-- Starred Status pills -->
            <div>
              <label class="mb-3 block text-sm font-medium text-ink-strong">Starred Status</label>
              <div class="flex flex-wrap gap-2">
                <button
                  v-for="opt in [{ value: 'all', label: 'All' }, { value: 'starred', label: 'Starred Only' }, { value: 'unstarred', label: 'Unstarred Only' }]"
                  :key="opt.value"
                  type="button"
                  class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
                  :class="starredFilter === opt.value ? 'bg-accent text-white' : 'bg-hairline text-ink-muted hover:text-ink-strong'"
                  @click="starredFilter = opt.value"
                >{{ opt.label }}</button>
              </div>
            </div>

            <!-- Last Updated pills -->
            <div>
              <label class="mb-3 block text-sm font-medium text-ink-strong">Last Updated</label>
              <div class="flex flex-wrap gap-2">
                <button
                  v-for="opt in [{ value: 'all', label: 'All Time' }, { value: 'today', label: 'Today' }, { value: 'week', label: 'This Week' }, { value: 'month', label: 'This Month' }, { value: 'year', label: 'This Year' }]"
                  :key="opt.value"
                  type="button"
                  class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
                  :class="lastUpdatedFilter === opt.value ? 'bg-accent text-white' : 'bg-hairline text-ink-muted hover:text-ink-strong'"
                  @click="lastUpdatedFilter = opt.value"
                >{{ opt.label }}</button>
              </div>
            </div>

            <!-- Sort dropdowns -->
            <div class="grid gap-6 md:grid-cols-2">
              <div>
                <label class="mb-2 block text-sm font-medium text-ink-strong">Sort by</label>
                <select v-model="sortBy" class="w-full rounded-2xl border border-hairline bg-transparent px-3 py-2.5 text-sm text-ink-strong outline-none transition focus:border-accent">
                  <option value="name">Name</option>
                  <option value="releases">Release count</option>
                  <option value="storage">Storage class</option>
                </select>
              </div>
              <div>
                <label class="mb-2 block text-sm font-medium text-ink-strong">Sort order</label>
                <select v-model="sortDirection" class="w-full rounded-2xl border border-hairline bg-transparent px-3 py-2.5 text-sm text-ink-strong outline-none transition focus:border-accent">
                  <option value="asc">Ascending</option>
                  <option value="desc">Descending</option>
                </select>
              </div>
            </div>
          </div>
          <div class="flex items-center justify-between border-t border-hairline px-6 py-5">
            <button class="text-sm font-medium text-ink-muted transition hover:text-ink-strong" @click="resetFilters">Reset All</button>
            <div class="flex items-center gap-3">
              <button type="button" class="rounded-full border border-hairline px-4 py-2.5 text-sm font-medium text-ink-muted transition hover:text-ink-strong" @click="filtersOpen = false">Cancel</button>
              <button type="button" class="rounded-full bg-accent px-4 py-2.5 text-sm font-semibold text-white transition hover:brightness-110" @click="filtersOpen = false">Apply Filters</button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <div v-if="createOpen" class="fixed inset-0 z-50 font-montserrat">
      <div class="absolute inset-0 bg-slate-950/50 backdrop-blur-sm" @click="closeCreate" />
      <div class="absolute inset-0 flex items-center justify-center px-4 py-6">
        <div class="w-full max-w-lg rounded-card border border-hairline bg-panel shadow-2xl">
          <div class="flex items-center justify-between border-b border-hairline px-6 py-5">
            <div class="flex items-center gap-3">
              <div class="flex size-9 items-center justify-center rounded-full bg-accent-soft text-accent">
                <IconGitBranch class="size-4" />
              </div>
              <span class="text-lg font-semibold text-ink-strong">New Repository</span>
            </div>
            <button class="text-ink-muted transition hover:text-ink-strong" @click="closeCreate">
              <IconX class="size-5" />
            </button>
          </div>

          <form class="space-y-4 px-6 py-6" @submit.prevent="submitCreate">
            <div>
              <label class="mb-1.5 block text-xs font-semibold uppercase tracking-wide text-ink-muted" for="repo-name">
                Name <span class="text-danger">*</span>
              </label>
              <input
                id="repo-name"
                class="h-11 w-full rounded-2xl border border-hairline bg-transparent px-4 text-sm text-ink-strong outline-none transition placeholder:text-ink-muted focus:border-accent"
                v-model.trim="form.name"
                required
                :disabled="isCreating"
                placeholder="e.g. my-app-releases"
              />
            </div>

            <div>
              <label class="mb-1.5 block text-xs font-semibold uppercase tracking-wide text-ink-muted" for="repo-description">
                Description
              </label>
              <input
                id="repo-description"
                class="h-11 w-full rounded-2xl border border-hairline bg-transparent px-4 text-sm text-ink-strong outline-none transition placeholder:text-ink-muted focus:border-accent"
                v-model.trim="form.description"
                :disabled="isCreating"
                placeholder="Optional description…"
              />
            </div>

            <div>
              <label class="mb-1.5 block text-xs font-semibold uppercase tracking-wide text-ink-muted" for="repo-storage-class">
                Storage Class <span class="text-danger">*</span>
              </label>
              <select
                id="repo-storage-class"
                class="h-11 w-full rounded-2xl border border-hairline bg-transparent px-3 text-sm text-ink-strong outline-none transition focus:border-accent"
                v-model="form.storageClassName"
                required
                :disabled="isCreating"
              >
                <option value="" disabled>Select a storage class</option>
                <option
                  v-for="sc in tenantSettingsStore.allowedStorageClasses"
                  :key="sc.name"
                  :value="sc.name"
                >{{ sc.name }}{{ sc.description ? ` — ${sc.description}` : '' }}</option>
              </select>
            </div>

            <div v-if="createError" class="rounded-2xl bg-danger-soft px-4 py-3 text-sm text-danger">
              {{ createError }}
            </div>

            <div class="flex items-center justify-end gap-3 pt-2">
              <button
                type="button"
                class="rounded-full border border-hairline px-4 py-2.5 text-sm font-medium text-ink-muted transition hover:text-ink-strong disabled:opacity-50"
                @click="closeCreate"
                :disabled="isCreating"
              >Cancel</button>
              <button
                type="submit"
                class="inline-flex items-center rounded-full bg-accent px-4 py-2.5 text-sm font-semibold text-white transition hover:brightness-110 disabled:opacity-50"
                :disabled="isCreating"
              >
                <svg v-if="isCreating" class="mr-1.5 h-4 w-4 animate-spin" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
                  <path class="opacity-75 fill-current" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
                </svg>
                {{ isCreating ? 'Creating…' : 'Create Repository' }}
              </button>
            </div>
          </form>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<script>
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useTenantStore } from '@/stores/tenant'
import { useAuthStore } from '@/stores/auth'
import { useTenantSettingsStore } from '@/stores/tenantSettings'
import { listRepositoriesWithReleaseStats, createRepository } from '@/api/repositories'
import {
  IconGitBranch,
  IconDatabase,
  IconPackage,
  IconPlus,
  IconSearch,
  IconFilter,
  IconLayoutGrid,
  IconListDetails,
  IconArrowRight,
  IconX,
  IconStar,
  IconStarFilled,
  IconClock,
} from '@tabler/icons-vue'

export default {
  name: 'TenantRepositories',
  components: {
    IconGitBranch,
    IconDatabase,
    IconPackage,
    IconPlus,
    IconSearch,
    IconFilter,
    IconLayoutGrid,
    IconListDetails,
    IconArrowRight,
    IconX,
    IconStar,
    IconStarFilled,
    IconClock,
  },
  setup() {
    const route = useRoute()
    const tenantStore = useTenantStore()
    const authStore = useAuthStore()
    const tenantSettingsStore = useTenantSettingsStore()

    const searchQuery = ref('')
    const viewMode = ref('grid')
    const filtersOpen = ref(false)
    const storageFilter = ref('all')
    const releasePresenceFilter = ref('all')
    const starredFilter = ref('all')
    const lastUpdatedFilter = ref('all')
    const sortBy = ref('name')
    const sortDirection = ref('asc')

    const tenantId = computed(() => route.params.tenantId ?? tenantStore.currentTenantId ?? '')
    const isTenantAdmin = computed(() =>
      tenantStore.currentTenant?.role === 'TenantAdmin' ?? false
    )

    const isLoading = ref(false)
    const error = ref(null)
    const items = ref([])

    // Starred repos (persisted in localStorage)
    const STARRED_KEY = computed(() => `binstash:starred:${tenantId.value}`)
    const starredRepoIds = ref(new Set())

    function loadStarred() {
      try {
        const stored = localStorage.getItem(STARRED_KEY.value)
        starredRepoIds.value = stored ? new Set(JSON.parse(stored)) : new Set()
      } catch {
        starredRepoIds.value = new Set()
      }
    }

    function saveStarred() {
      localStorage.setItem(STARRED_KEY.value, JSON.stringify([...starredRepoIds.value]))
    }

    function toggleStar(repoId, event) {
      if (event) {
        event.preventDefault()
        event.stopPropagation()
      }
      if (starredRepoIds.value.has(repoId)) {
        starredRepoIds.value.delete(repoId)
      } else {
        starredRepoIds.value.add(repoId)
      }
      starredRepoIds.value = new Set(starredRepoIds.value) // trigger reactivity
      saveStarred()
    }

    // Release counts per repo
    const releaseCountMap = ref({})

    const totalReleaseCount = computed(() =>
      Object.values(releaseCountMap.value).reduce((sum, count) => sum + Number(count || 0), 0),
    )

    const storageClassOptions = computed(() =>
      [...new Set(items.value.map(repo => repo.storageClass).filter(Boolean))].sort((a, b) => a.localeCompare(b)),
    )

    const filteredItems = computed(() => {
      const q = searchQuery.value.toLowerCase().trim()
      const searched = items.value.filter(r => {
        const matchesQuery = !q ||
          r.name.toLowerCase().includes(q) ||
          (r.description ?? '').toLowerCase().includes(q) ||
          r.storageClass.toLowerCase().includes(q)
        const matchesStorage = storageFilter.value === 'all' || r.storageClass === storageFilter.value
        const releaseCount = Number(releaseCountMap.value[r.id] ?? 0)
        const matchesReleasePresence = releasePresenceFilter.value === 'all' ||
          (releasePresenceFilter.value === 'with' && releaseCount > 0) ||
          (releasePresenceFilter.value === 'without' && releaseCount === 0)
        const matchesStarred = starredFilter.value === 'all' ||
          (starredFilter.value === 'starred' && starredRepoIds.value.has(r.id)) ||
          (starredFilter.value === 'unstarred' && !starredRepoIds.value.has(r.id))
        return matchesQuery && matchesStorage && matchesReleasePresence && matchesStarred
      })

      return searched.slice().sort((a, b) => {
        const direction = sortDirection.value === 'asc' ? 1 : -1
        if (sortBy.value === 'releases') {
          return ((releaseCountMap.value[a.id] ?? 0) - (releaseCountMap.value[b.id] ?? 0)) * direction
        }
        if (sortBy.value === 'storage') {
          return a.storageClass.localeCompare(b.storageClass) * direction
        }
        return a.name.localeCompare(b.name) * direction
      })
    })

    function resetFilters() {
      storageFilter.value = 'all'
      releasePresenceFilter.value = 'all'
      starredFilter.value = 'all'
      lastUpdatedFilter.value = 'all'
      sortBy.value = 'name'
      sortDirection.value = 'asc'
    }

    async function load() {
      isLoading.value = true
      error.value = null
      releaseCountMap.value = {}
      try {
        const data = await listRepositoriesWithReleaseStats(1)
        items.value = data.map((x) => x.repository)
        releaseCountMap.value = data.reduce((acc, x) => {
          acc[x.repository.id] = x.releaseCount
          return acc
        }, {})
      } catch (e) {
        error.value = e instanceof Error ? e.message : 'Could not load repositories.'
      } finally {
        isLoading.value = false
      }
    }

    // Create modal
    const createOpen = ref(false)
    const isCreating = ref(false)
    const createError = ref(null)
    const form = ref({ name: '', description: '', storageClassName: '' })

    function openCreate() {
      createError.value = null
      form.value = { name: '', description: '', storageClassName: tenantSettingsStore.allowedStorageClasses.find(sc => sc.isDefault)?.name ?? '' }
      createOpen.value = true
    }

    function closeCreate() {
      if (isCreating.value) return
      createOpen.value = false
    }

    async function submitCreate() {
      createError.value = null
      isCreating.value = true
      try {
        await createRepository({
          name: form.value.name,
          description: form.value.description || null,
          storageClassName: form.value.storageClassName,
        })
        createOpen.value = false
        await load()
      } catch (e) {
        createError.value = e instanceof Error ? e.message : 'Could not create repository.'
      } finally {
        isCreating.value = false
      }
    }

    watch(() => tenantId.value, () => {
      items.value = []
      loadStarred()
      load()
    })

    onMounted(() => {
      loadStarred()
      load()
    })

    return {
      searchQuery,
      tenantId,
      isTenantAdmin,
      isLoading,
      error,
      filteredItems,
      releaseCountMap,
      totalReleaseCount,
      storageClassOptions,
      viewMode,
      filtersOpen,
      storageFilter,
      releasePresenceFilter,
      starredFilter,
      lastUpdatedFilter,
      sortBy,
      sortDirection,
      resetFilters,
      tenantSettingsStore,
      createOpen,
      isCreating,
      createError,
      form,
      openCreate,
      closeCreate,
      starredRepoIds,
      toggleStar,
      submitCreate,
    }
  },
}
</script>