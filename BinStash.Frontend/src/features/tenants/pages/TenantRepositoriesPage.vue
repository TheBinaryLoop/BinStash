<template>
  <!-- Page header -->
  <div class="mb-8 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
    <div>
      <h1 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white md:text-[32px]">Repositories</h1>
      <p class="mt-2 text-sm text-slate-500 dark:text-slate-400">Browse and manage repositories for this tenant.</p>
    </div>
    <div v-if="isTenantAdmin" class="flex items-center gap-2">
      <button
        class="inline-flex items-center gap-2 rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-violet-500/20 transition hover:bg-[#6d78ff]"
        @click="openCreate"
      >
        <IconPlus class="h-4 w-4 shrink-0" />
        <span>New Repository</span>
      </button>
    </div>
  </div>

  <!-- KPI cards -->
  <div class="mb-6 grid grid-cols-12 gap-5">
    <div class="col-span-full md:col-span-4 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
      <div class="mb-4 flex items-center justify-between">
        <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Repositories</div>
        <div class="flex h-10 w-10 items-center justify-center rounded-full bg-[#7C86FF]/10 text-[#7C86FF]"><IconGitBranch class="h-5 w-5" /></div>
      </div>
      <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">{{ filteredItems.length }}</div>
      <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Visible repositories</div>
    </div>
    <div class="col-span-full md:col-span-4 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
      <div class="mb-4 flex items-center justify-between">
        <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Releases</div>
        <div class="flex h-10 w-10 items-center justify-center rounded-full bg-emerald-500/10 text-emerald-500"><IconPackage class="h-5 w-5" /></div>
      </div>
      <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">{{ totalReleaseCount }}</div>
      <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Across visible repositories</div>
    </div>
    <div class="col-span-full md:col-span-4 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
      <div class="mb-4 flex items-center justify-between">
        <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Starred</div>
        <div class="flex h-10 w-10 items-center justify-center rounded-full bg-amber-500/10 text-amber-500"><IconStar class="h-5 w-5" /></div>
      </div>
      <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">{{ starredRepoIds.size }}</div>
      <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Starred repositories</div>
    </div>
  </div>

  <!-- Search / filters / view mode -->
  <div class="mb-6 rounded-[28px] border border-slate-200 bg-white p-4 shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
    <div class="flex flex-col gap-3 xl:flex-row xl:items-center xl:justify-between">
      <div class="flex flex-1 flex-col gap-3 sm:flex-row sm:items-center">
        <div class="relative flex-1 sm:max-w-md">
          <IconSearch class="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-slate-400 dark:text-slate-500" />
          <input
            v-model="searchQuery"
            type="search"
            placeholder="Search repositories..."
            class="w-full rounded-full border border-slate-200 bg-slate-50 py-3 pl-10 pr-4 text-sm text-slate-900 outline-none transition placeholder:text-slate-400 focus:border-[#7C86FF] focus:bg-white dark:border-white/10 dark:bg-white/[0.03] dark:text-white dark:placeholder:text-slate-500"
          />
        </div>
        <button
          type="button"
          class="inline-flex items-center justify-center gap-2 rounded-full border border-slate-200 bg-slate-50 px-4 py-3 text-sm font-medium text-slate-600 transition hover:border-slate-300 hover:bg-slate-100 dark:border-white/10 dark:bg-white/[0.03] dark:text-slate-300 dark:hover:bg-white/[0.06]"
          @click="filtersOpen = true"
        >
          <IconFilter class="h-4 w-4" />
          <span>Filters</span>
        </button>
      </div>
      <div class="flex items-center gap-2 self-start rounded-full border border-slate-200 bg-slate-50 p-1 dark:border-white/10 dark:bg-white/[0.03]">
        <button
          type="button"
          class="inline-flex items-center gap-2 rounded-full px-3 py-2 text-sm font-medium transition"
          :class="viewMode === 'grid' ? 'bg-[#7C86FF] text-white' : 'text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-white'"
          @click="viewMode = 'grid'"
        >
          <IconLayoutGrid class="h-4 w-4" /> Grid
        </button>
        <button
          type="button"
          class="inline-flex items-center gap-2 rounded-full px-3 py-2 text-sm font-medium transition"
          :class="viewMode === 'list' ? 'bg-[#7C86FF] text-white' : 'text-slate-500 hover:text-slate-700 dark:text-slate-400 dark:hover:text-white'"
          @click="viewMode = 'list'"
        >
          <IconListDetails class="h-4 w-4" /> List
        </button>
      </div>
    </div>
  </div>

  <!-- Loading -->
  <div v-if="isLoading" class="flex items-center justify-center py-20">
    <svg class="animate-spin w-8 h-8 text-violet-500" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
      <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
      <path class="opacity-75 fill-current" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
    </svg>
  </div>

  <!-- Error -->
  <div v-else-if="error" class="bg-rose-50 dark:bg-rose-500/10 text-rose-700 dark:text-rose-300 rounded-xl px-5 py-4 text-sm">
    {{ error }}
  </div>

  <!-- Empty -->
  <div v-else-if="filteredItems.length === 0" class="py-20 text-center">
    <div class="mx-auto mb-4 flex h-16 w-16 items-center justify-center rounded-2xl bg-[#7C86FF]/10">
      <IconGitBranch class="h-8 w-8 text-[#7C86FF]" />
    </div>
    <div class="mb-1 text-lg font-semibold text-slate-900 dark:text-white">
      {{ searchQuery ? 'No results found' : 'No repositories yet' }}
    </div>
    <div class="mb-5 text-sm text-slate-500 dark:text-slate-400">
      {{ searchQuery
        ? 'Try adjusting your search term.'
        : 'Create your first repository to start storing releases.' }}
    </div>
    <button
      v-if="!searchQuery && isTenantAdmin"
      class="inline-flex items-center gap-2 rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-violet-500/20 transition hover:bg-[#6d78ff]"
      @click="openCreate"
    >
      <IconPlus class="h-4 w-4 shrink-0" />
      Create Repository
    </button>
  </div>

  <!-- Repository grid -->
  <div v-else-if="viewMode === 'grid'" class="grid grid-cols-12 gap-5">
    <router-link
      v-for="repo in filteredItems"
      :key="repo.id"
      :to="`/t/${tenantId}/repositories/${repo.id}`"
      class="group col-span-full overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm transition-all duration-200 hover:-translate-y-0.5 hover:border-violet-300 hover:shadow-lg dark:border-white/5 dark:bg-[#0F172D] dark:hover:border-violet-500/40"
      :class="viewMode === 'grid' ? 'sm:col-span-6 lg:col-span-4 xl:col-span-3' : ''"
    >
      <!-- Card header stripe -->
      <div class="h-1.5 w-full bg-linear-to-r from-[#615FFF] to-[#9810FA] opacity-80 transition group-hover:opacity-100" />

      <div class="p-5">
        <!-- Icon + name + star -->
        <div class="mb-3 flex items-start gap-3">
          <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-2xl bg-[#7C86FF]/10 text-[#7C86FF] transition group-hover:bg-[#7C86FF]/20">
            <IconGitBranch class="h-5 w-5" />
          </div>
          <div class="flex-1 min-w-0">
            <div class="flex items-center gap-2">
              <div class="truncate text-sm font-bold text-slate-900 transition group-hover:text-[#7C86FF] dark:text-white">{{ repo.name }}</div>
              <button
                type="button"
                class="shrink-0 transition hover:scale-110"
                @click="toggleStar(repo.id, $event)"
              >
                <IconStarFilled v-if="starredRepoIds.has(repo.id)" class="h-4 w-4 text-amber-400" />
                <IconStar v-else class="h-4 w-4 text-slate-300 dark:text-slate-600 hover:text-amber-400" />
              </button>
            </div>
            <div class="mt-1.5 flex items-center gap-1.5">
              <span class="inline-flex items-center rounded-full bg-slate-100 px-2 py-0.5 text-[11px] font-medium text-slate-500 dark:bg-[#1E2939] dark:text-slate-300">
                {{ repo.storageClass }}
              </span>
            </div>
          </div>
        </div>

        <!-- Description -->
        <p class="min-h-10 line-clamp-2 text-xs text-slate-500 dark:text-slate-400">
          {{ repo.description || 'No description provided.' }}
        </p>

        <!-- Footer -->
        <div class="mt-4 flex items-center justify-between border-t border-slate-100 pt-3 dark:border-white/5">
          <div class="flex items-center gap-3 text-xs text-slate-400 dark:text-slate-500">
            <span class="inline-flex items-center gap-1">
              <IconPackage class="h-3.5 w-3.5 shrink-0" />
              {{ releaseCountMap[repo.id] ?? '—' }} Releases
            </span>
            <span class="inline-flex items-center gap-1">
              <IconClock class="h-3.5 w-3.5 shrink-0" />
              Updated recently
            </span>
          </div>
          <span class="flex items-center gap-0.5 text-xs font-medium text-[#7C86FF] transition group-hover:text-[#6974ff]">
            Open <IconArrowRight class="h-3.5 w-3.5" />
          </span>
        </div>
      </div>
    </router-link>
  </div>

  <!-- Repository list -->
  <div v-else class="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
    <div class="overflow-x-auto">
      <table class="min-w-full divide-y divide-slate-200 dark:divide-white/5">
        <thead class="bg-slate-50 dark:bg-white/3">
          <tr>
            <th class="px-5 py-4 text-left text-xs font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Repository</th>
            <th class="px-5 py-4 text-left text-xs font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Storage</th>
            <th class="px-5 py-4 text-left text-xs font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Description</th>
            <th class="px-5 py-4 text-left text-xs font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Releases</th>
            <th class="px-5 py-4 text-left text-xs font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Last Updated</th>
            <th class="px-5 py-4"></th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100 dark:divide-white/5">
          <tr v-for="repo in filteredItems" :key="repo.id" class="transition hover:bg-slate-50 dark:hover:bg-white/3">
            <td class="px-5 py-4">
              <router-link :to="`/t/${tenantId}/repositories/${repo.id}`" class="flex items-center gap-3">
                <button
                  type="button"
                  class="shrink-0 transition hover:scale-110"
                  @click.prevent="toggleStar(repo.id, $event)"
                >
                  <IconStarFilled v-if="starredRepoIds.has(repo.id)" class="h-4 w-4 text-amber-400" />
                  <IconStar v-else class="h-4 w-4 text-slate-300 dark:text-slate-600" />
                </button>
                <div class="flex h-10 w-10 items-center justify-center rounded-2xl bg-[#7C86FF]/10 text-[#7C86FF]"><IconGitBranch class="h-5 w-5" /></div>
                <div>
                  <div class="text-sm font-semibold text-slate-900 dark:text-white">{{ repo.name }}</div>
                </div>
              </router-link>
            </td>
            <td class="px-5 py-4">
              <span class="inline-flex items-center rounded-full bg-slate-100 px-2.5 py-0.5 text-xs font-medium text-slate-500 dark:bg-[#1E2939] dark:text-slate-300">
                {{ repo.storageClass }}
              </span>
            </td>
            <td class="px-5 py-4 text-sm text-slate-500 dark:text-slate-400">{{ repo.description || 'No description provided.' }}</td>
            <td class="px-5 py-4">
              <div class="flex items-center gap-1.5 text-sm text-slate-900 dark:text-white">
                <IconPackage class="h-3.5 w-3.5 text-slate-400" />
                {{ releaseCountMap[repo.id] ?? 0 }}
              </div>
            </td>
            <td class="px-5 py-4">
              <div class="flex items-center gap-1.5 text-sm text-slate-500 dark:text-slate-400">
                <IconClock class="h-3.5 w-3.5" />
                Recently
              </div>
            </td>
            <td class="px-5 py-4 text-right">
              <router-link :to="`/t/${tenantId}/repositories/${repo.id}`" class="inline-flex items-center gap-1 text-sm font-medium text-[#7C86FF] hover:text-[#6974ff]">
                Open <IconArrowRight class="h-4 w-4" />
              </router-link>
            </td>
          </tr>
        </tbody>
      </table>
    </div>
  </div>

  <!-- Create repository modal -->
  <Teleport to="body">
    <div v-if="filtersOpen" class="fixed inset-0 z-50">
      <div class="absolute inset-0 bg-slate-950/50 backdrop-blur-sm" @click="filtersOpen = false" />
      <div class="absolute inset-0 flex items-center justify-center px-4 py-6">
        <div class="w-full max-w-2xl rounded-[28px] border border-slate-200 bg-white shadow-2xl dark:border-white/10 dark:bg-[#101935]">
          <div class="flex items-center justify-between border-b border-slate-200 px-6 py-5 dark:border-white/5">
            <div>
              <h3 class="text-lg font-semibold text-slate-900 dark:text-white">Filter Repositories</h3>
              <p class="mt-1 text-sm text-slate-500 dark:text-slate-400">Refine your repository search with advanced filters</p>
            </div>
            <button class="text-slate-400 transition hover:text-slate-600 dark:hover:text-white" @click="filtersOpen = false">
              <IconX class="h-5 w-5" />
            </button>
          </div>
          <div class="space-y-6 px-6 py-6">
            <!-- Storage class pills -->
            <div>
              <label class="mb-3 block text-sm font-medium text-slate-700 dark:text-slate-300">Storage Class</label>
              <div class="flex flex-wrap gap-2">
                <button
                  type="button"
                  class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
                  :class="storageFilter === 'all' ? 'bg-[#7C86FF] text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200 dark:bg-[#1E2939] dark:text-slate-300 dark:hover:bg-[#253350]'"
                  @click="storageFilter = 'all'"
                >All</button>
                <button
                  v-for="storage in storageClassOptions"
                  :key="storage"
                  type="button"
                  class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
                  :class="storageFilter === storage ? 'bg-[#7C86FF] text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200 dark:bg-[#1E2939] dark:text-slate-300 dark:hover:bg-[#253350]'"
                  @click="storageFilter = storage"
                >{{ storage }}</button>
              </div>
            </div>

            <!-- Releases pills -->
            <div>
              <label class="mb-3 block text-sm font-medium text-slate-700 dark:text-slate-300">Releases</label>
              <div class="flex flex-wrap gap-2">
                <button
                  v-for="opt in [{ value: 'all', label: 'All' }, { value: 'with', label: 'With Releases' }, { value: 'without', label: 'Without Releases' }]"
                  :key="opt.value"
                  type="button"
                  class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
                  :class="releasePresenceFilter === opt.value ? 'bg-[#7C86FF] text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200 dark:bg-[#1E2939] dark:text-slate-300 dark:hover:bg-[#253350]'"
                  @click="releasePresenceFilter = opt.value"
                >{{ opt.label }}</button>
              </div>
            </div>

            <!-- Starred Status pills -->
            <div>
              <label class="mb-3 block text-sm font-medium text-slate-700 dark:text-slate-300">Starred Status</label>
              <div class="flex flex-wrap gap-2">
                <button
                  v-for="opt in [{ value: 'all', label: 'All' }, { value: 'starred', label: 'Starred Only' }, { value: 'unstarred', label: 'Unstarred Only' }]"
                  :key="opt.value"
                  type="button"
                  class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
                  :class="starredFilter === opt.value ? 'bg-[#7C86FF] text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200 dark:bg-[#1E2939] dark:text-slate-300 dark:hover:bg-[#253350]'"
                  @click="starredFilter = opt.value"
                >{{ opt.label }}</button>
              </div>
            </div>

            <!-- Last Updated pills -->
            <div>
              <label class="mb-3 block text-sm font-medium text-slate-700 dark:text-slate-300">Last Updated</label>
              <div class="flex flex-wrap gap-2">
                <button
                  v-for="opt in [{ value: 'all', label: 'All Time' }, { value: 'today', label: 'Today' }, { value: 'week', label: 'This Week' }, { value: 'month', label: 'This Month' }, { value: 'year', label: 'This Year' }]"
                  :key="opt.value"
                  type="button"
                  class="rounded-full px-3.5 py-1.5 text-sm font-medium transition"
                  :class="lastUpdatedFilter === opt.value ? 'bg-[#7C86FF] text-white' : 'bg-slate-100 text-slate-600 hover:bg-slate-200 dark:bg-[#1E2939] dark:text-slate-300 dark:hover:bg-[#253350]'"
                  @click="lastUpdatedFilter = opt.value"
                >{{ opt.label }}</button>
              </div>
            </div>

            <!-- Sort dropdowns -->
            <div class="grid gap-6 md:grid-cols-2">
              <div>
                <label class="mb-2 block text-sm font-medium text-slate-700 dark:text-slate-300">Sort by</label>
                <select v-model="sortBy" class="form-select w-full rounded-2xl border-slate-200 dark:border-white/10 dark:bg-white/3">
                  <option value="name">Name</option>
                  <option value="releases">Release count</option>
                  <option value="storage">Storage class</option>
                </select>
              </div>
              <div>
                <label class="mb-2 block text-sm font-medium text-slate-700 dark:text-slate-300">Sort order</label>
                <select v-model="sortDirection" class="form-select w-full rounded-2xl border-slate-200 dark:border-white/10 dark:bg-white/3">
                  <option value="asc">Ascending</option>
                  <option value="desc">Descending</option>
                </select>
              </div>
            </div>
          </div>
          <div class="flex items-center justify-between border-t border-slate-200 px-6 py-5 dark:border-white/5">
            <button class="text-sm font-medium text-slate-500 transition hover:text-slate-700 dark:text-slate-400 dark:hover:text-white" @click="resetFilters">Reset All</button>
            <div class="flex items-center gap-3">
              <button type="button" class="rounded-full border border-slate-200 px-4 py-2.5 text-sm font-medium text-slate-600 transition hover:bg-slate-50 dark:border-white/10 dark:text-slate-300 dark:hover:bg-white/3" @click="filtersOpen = false">Cancel</button>
              <button type="button" class="rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white transition hover:bg-[#6d78ff]" @click="filtersOpen = false">Apply Filters</button>
            </div>
          </div>
        </div>
      </div>
    </div>

    <div v-if="createOpen" class="fixed inset-0 z-50">
      <div class="absolute inset-0 bg-slate-900/40" @click="closeCreate" />
      <div class="absolute inset-0 flex items-center justify-center px-4 py-6">
        <div class="bg-white dark:bg-gray-800 rounded-xl shadow-xl w-full max-w-lg border border-gray-200 dark:border-gray-700/60">
          <div class="px-6 py-4 border-b border-gray-200 dark:border-gray-700/60 flex items-center justify-between">
            <div class="flex items-center gap-2">
              <div class="w-8 h-8 rounded-lg bg-teal-100 dark:bg-teal-500/20 flex items-center justify-center">
                <IconGitBranch class="text-teal-500 w-4 h-4" />
              </div>
              <span class="font-semibold text-gray-800 dark:text-gray-100">New Repository</span>
            </div>
            <button class="text-gray-400 hover:text-gray-500 dark:hover:text-gray-300 transition" @click="closeCreate">
              <IconX class="w-5 h-5" />
            </button>
          </div>

          <form class="px-6 py-5 space-y-4" @submit.prevent="submitCreate">
            <div>
              <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5" for="repo-name">
                Name <span class="text-rose-500">*</span>
              </label>
              <input
                id="repo-name"
                class="form-input w-full"
                v-model.trim="form.name"
                required
                :disabled="isCreating"
                placeholder="e.g. my-app-releases"
              />
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5" for="repo-description">
                Description
              </label>
              <input
                id="repo-description"
                class="form-input w-full"
                v-model.trim="form.description"
                :disabled="isCreating"
                placeholder="Optional description…"
              />
            </div>

            <div>
              <label class="block text-sm font-medium text-gray-700 dark:text-gray-300 mb-1.5" for="repo-storage-class">
                Storage Class <span class="text-rose-500">*</span>
              </label>
              <select
                id="repo-storage-class"
                class="form-select w-full"
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

            <div v-if="createError" class="bg-rose-50 dark:bg-rose-500/10 text-rose-700 dark:text-rose-300 px-4 py-3 rounded-lg text-sm">
              {{ createError }}
            </div>

            <div class="flex items-center justify-end gap-3 pt-2">
              <button
                type="button"
                class="btn border-gray-200 hover:border-gray-300 dark:border-gray-700/60 dark:hover:border-gray-600"
                @click="closeCreate"
                :disabled="isCreating"
              >Cancel</button>
              <button
                type="submit"
                class="btn bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white"
                :disabled="isCreating"
              >
                <svg v-if="isCreating" class="animate-spin w-4 h-4 mr-1.5" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
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