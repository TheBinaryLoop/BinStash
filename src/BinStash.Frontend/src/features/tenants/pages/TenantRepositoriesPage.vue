<template>
  <div>
  <PageHeader title="Repositories" description="Browse and manage repositories for this tenant.">
    <template v-if="isTenantAdmin" #actions>
      <BaseButton :icon="IconPlus" @click="openCreate">New Repository</BaseButton>
    </template>
  </PageHeader>

  <div class="space-y-6">
    <!-- KPI cards -->
    <div class="grid gap-4 md:grid-cols-3">
      <StatCard label="Total Repositories" :value="filteredItems.length" :icon="IconGitBranch" tone="accent" />
      <StatCard label="Total Releases" :value="totalReleaseCount" :icon="IconPackage" tone="success" />
      <StatCard label="Starred Repositories" :value="starredRepoIds.size" :icon="IconStar" tone="warning" />
    </div>

    <!-- Search / filters / view mode -->
    <div class="flex flex-col gap-3 sm:flex-row sm:items-center">
      <div class="flex-1">
        <BaseInput
          v-model="searchQuery"
          type="search"
          placeholder="Search repositories..."
          :prefix-icon="IconSearch"
        />
      </div>
      <BaseButton variant="secondary" :icon="IconFilter" @click="filtersOpen = true">Filters</BaseButton>
      <div class="flex h-10 items-center gap-1 self-start rounded-control border border-hairline px-1">
        <button
          type="button"
          class="flex size-8 items-center justify-center rounded-control transition"
          :class="viewMode === 'grid' ? 'bg-accent-soft text-accent' : 'text-ink-muted hover:text-ink-strong'"
          aria-label="Grid view"
          @click="viewMode = 'grid'"
        >
          <IconLayoutGrid class="size-4" />
        </button>
        <button
          type="button"
          class="flex size-8 items-center justify-center rounded-control transition"
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
      <Spinner :size="32" color="var(--color-accent)" />
    </div>

    <!-- Error -->
    <div v-else-if="error" class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
      {{ error }}
    </div>

    <!-- Empty -->
    <EmptyState
      v-else-if="filteredItems.length === 0"
      :icon="IconGitBranch"
      :title="searchQuery ? 'No results found' : 'No repositories yet'"
      :description="searchQuery
        ? 'Try adjusting your search term.'
        : 'Create your first repository to start storing releases.'"
    >
      <BaseButton v-if="!searchQuery && isTenantAdmin" :icon="IconPlus" @click="openCreate">Create Repository</BaseButton>
    </EmptyState>

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
              <BaseBadge class="mt-1.5">{{ repo.storageClass }}</BaseBadge>
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
      <DataTable :columns="listColumns" :items="filteredItems" empty="No repositories.">
        <template #cell-name="{ item }">
          <router-link :to="`/t/${tenantId}/repositories/${item.id}`" class="flex items-center gap-3">
            <button
              type="button"
              class="shrink-0 transition hover:scale-110"
              @click.prevent="toggleStar(item.id, $event)"
            >
              <IconStarFilled v-if="starredRepoIds.has(item.id)" class="size-4 text-warning" />
              <IconStar v-else class="size-4 text-ink-subtle" />
            </button>
            <div class="flex size-10 items-center justify-center rounded-full bg-accent-soft text-accent"><IconGitBranch class="size-5" /></div>
            <div class="text-sm font-semibold text-ink-strong">{{ item.name }}</div>
          </router-link>
        </template>
        <template #cell-storageClass="{ item }">
          <BaseBadge>{{ item.storageClass }}</BaseBadge>
        </template>
        <template #cell-description="{ item }">
          {{ item.description || 'No description provided.' }}
        </template>
        <template #cell-releases="{ item }">
          <div class="flex items-center gap-1.5 text-sm text-ink-strong">
            <IconPackage class="size-3.5 text-ink-muted" />
            {{ releaseCountMap[item.id] ?? 0 }}
          </div>
        </template>
        <template #cell-updated>
          <div class="flex items-center gap-1.5 text-sm text-ink-muted">
            <IconClock class="size-3.5" />
            Recently
          </div>
        </template>
        <template #cell-actions="{ item }">
          <router-link :to="`/t/${tenantId}/repositories/${item.id}`" class="inline-flex items-center gap-1 text-sm font-medium text-accent">
            Open <IconArrowRight class="size-4" />
          </router-link>
        </template>
      </DataTable>
    </div>
  </div>

  <!-- Filters modal -->
  <BaseModal v-model:open="filtersOpen" title="Filter Repositories" description="Refine your repository search with advanced filters" size="lg">
    <div class="space-y-6">
      <!-- Storage class pills -->
      <div>
        <label class="mb-3 block text-sm font-medium text-ink-strong">Storage Class</label>
        <div class="flex flex-wrap gap-2">
          <button
            type="button"
            class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
            :class="storageFilter === 'all' ? 'bg-accent text-white' : 'bg-raised text-ink-muted hover:text-ink-strong'"
            @click="storageFilter = 'all'"
          >All</button>
          <button
            v-for="storage in storageClassOptions"
            :key="storage"
            type="button"
            class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
            :class="storageFilter === storage ? 'bg-accent text-white' : 'bg-raised text-ink-muted hover:text-ink-strong'"
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
            :class="releasePresenceFilter === opt.value ? 'bg-accent text-white' : 'bg-raised text-ink-muted hover:text-ink-strong'"
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
            :class="starredFilter === opt.value ? 'bg-accent text-white' : 'bg-raised text-ink-muted hover:text-ink-strong'"
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
            :class="lastUpdatedFilter === opt.value ? 'bg-accent text-white' : 'bg-raised text-ink-muted hover:text-ink-strong'"
            @click="lastUpdatedFilter = opt.value"
          >{{ opt.label }}</button>
        </div>
      </div>

      <!-- Sort dropdowns -->
      <div class="grid gap-6 md:grid-cols-2">
        <BaseSelect
          v-model="sortBy"
          label="Sort by"
          :options="[
            { value: 'name', label: 'Name' },
            { value: 'releases', label: 'Release count' },
            { value: 'storage', label: 'Storage class' },
          ]"
        />
        <BaseSelect
          v-model="sortDirection"
          label="Sort order"
          :options="[
            { value: 'asc', label: 'Ascending' },
            { value: 'desc', label: 'Descending' },
          ]"
        />
      </div>
    </div>

    <template #footer>
      <BaseButton variant="ghost" class="mr-auto" @click="resetFilters">Reset All</BaseButton>
      <BaseButton variant="secondary" @click="filtersOpen = false">Cancel</BaseButton>
      <BaseButton @click="filtersOpen = false">Apply Filters</BaseButton>
    </template>
  </BaseModal>

  <!-- Create modal -->
  <BaseModal v-model:open="createOpen" size="md" @close="closeCreate">
    <template #header>
      <div class="flex items-center gap-3">
        <div class="flex size-9 items-center justify-center rounded-full bg-accent-soft text-accent">
          <IconGitBranch class="size-4" />
        </div>
        <span class="text-base font-semibold text-ink-strong">New Repository</span>
      </div>
    </template>

    <form id="create-repo-form" class="space-y-4" @submit.prevent="submitCreate">
      <BaseInput
        v-model.trim="form.name"
        label="Name"
        required
        :disabled="isCreating"
        placeholder="e.g. my-app-releases"
      />

      <BaseInput
        v-model.trim="form.description"
        label="Description"
        :disabled="isCreating"
        placeholder="Optional description…"
      />

      <BaseSelect
        v-model="form.storageClassName"
        label="Storage Class"
        required
        :disabled="isCreating"
        placeholder="Select a storage class"
      >
        <option
          v-for="sc in tenantSettingsStore.allowedStorageClasses"
          :key="sc.name"
          :value="sc.name"
        >{{ sc.name }}{{ sc.description ? ` — ${sc.description}` : '' }}</option>
      </BaseSelect>

      <div v-if="createError" class="rounded-control border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
        {{ createError }}
      </div>
    </form>

    <template #footer>
      <BaseButton variant="secondary" :disabled="isCreating" @click="closeCreate">Cancel</BaseButton>
      <BaseButton type="submit" form="create-repo-form" :loading="isCreating">
        {{ isCreating ? 'Creating…' : 'Create Repository' }}
      </BaseButton>
    </template>
  </BaseModal>
  </div>
</template>

<script>
import { ref, computed, onMounted, watch } from 'vue'
import { useRoute } from 'vue-router'
import { useTenantStore } from '@/stores/tenant'
import { useAuthStore } from '@/stores/auth'
import { useTenantSettingsStore } from '@/stores/tenantSettings'
import { listRepositoriesWithReleaseStats, createRepository } from '@/api/repositories'
import { useToast } from '@/composables/useToast'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import {
  PageHeader,
  StatCard,
  BaseButton,
  BaseInput,
  BaseSelect,
  BaseBadge,
  BaseModal,
  DataTable,
  EmptyState,
} from '@/shared/components/ui'
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
    PageHeader,
    StatCard,
    BaseButton,
    BaseInput,
    BaseSelect,
    BaseBadge,
    BaseModal,
    DataTable,
    EmptyState,
    Spinner,
  },
  setup() {
    const route = useRoute()
    const tenantStore = useTenantStore()
    const authStore = useAuthStore()
    const tenantSettingsStore = useTenantSettingsStore()
    const toast = useToast()

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

    const listColumns = [
      { key: 'name', label: 'Repository' },
      { key: 'storageClass', label: 'Storage' },
      { key: 'description', label: 'Description' },
      { key: 'releases', label: 'Releases' },
      { key: 'updated', label: 'Last Updated' },
      { key: 'actions', label: '', align: 'right' },
    ]

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
        toast.success('Repository created')
        await load()
      } catch (e) {
        createError.value = e instanceof Error ? e.message : 'Could not create repository.'
        toast.error(createError.value)
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
      listColumns,
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
      // Icons exposed for template :icon bindings
      IconGitBranch,
      IconPackage,
      IconStar,
      IconStarFilled,
      IconPlus,
      IconSearch,
      IconFilter,
      IconLayoutGrid,
      IconListDetails,
      IconArrowRight,
      IconClock,
    }
  },
}
</script>
