<template>
  <!-- Page header -->
  <div class="mb-8 flex flex-col gap-4 lg:flex-row lg:items-start lg:justify-between">
    <div>
      <h1 class="text-3xl font-bold tracking-tight text-slate-900 dark:text-white md:text-[32px]">
        Instance Overview
      </h1>
      <p class="mt-2 text-sm text-slate-500 dark:text-slate-400">
        System-wide administration and monitoring.
      </p>
    </div>
  </div>

  <!-- Stats cards -->
  <div class="mb-8 grid grid-cols-12 gap-5">

    <!-- Tenants card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
      <div>
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Tenants</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-[#7C86FF]/10 text-[#7C86FF]">
            <IconBuildingSkyscraper class="h-5 w-5" />
          </div>
        </div>
        <div class="flex items-end justify-between gap-4">
          <div>
            <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">{{ stats?.tenantCount ?? '—' }}</div>
            <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Total tenants</div>
          </div>
          <router-link
            to="/instance/tenants"
            class="text-xs font-semibold text-[#7C86FF] transition hover:text-[#6974ff]"
          >View all →</router-link>
        </div>
      </div>
    </div>

    <!-- Users card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
      <div>
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Users</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-sky-500/10 text-sky-500">
            <IconUser class="h-5 w-5" />
          </div>
        </div>
        <div class="flex items-end justify-between gap-4">
          <div>
            <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">{{ stats?.userCount ?? '—' }}</div>
            <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Registered users</div>
          </div>
          <router-link
            to="/instance/users"
            class="text-xs font-semibold text-[#7C86FF] transition hover:text-[#6974ff]"
          >View all →</router-link>
        </div>
      </div>
    </div>

    <!-- Repositories card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
      <div>
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Repositories</div>
          <div class="flex h-10 w-10 items-center justify-center rounded-full bg-emerald-500/10 text-emerald-500">
            <IconGitBranch class="h-5 w-5" />
          </div>
        </div>
        <div>
          <div class="text-[40px] font-bold leading-none text-slate-900 dark:text-white">{{ stats?.repositoryCount ?? '—' }}</div>
          <div class="mt-2 text-xs text-slate-500 dark:text-slate-400">Total repositories</div>
        </div>
      </div>
    </div>

    <!-- System Status card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-[28px] border border-slate-200 bg-white p-5 shadow-sm transition-colors dark:border-white/5 dark:bg-[#0F172D]">
      <div>
        <div class="mb-5 flex items-center justify-between">
          <div class="text-sm font-semibold uppercase tracking-wide text-slate-400 dark:text-slate-500">Status</div>
          <div
            class="flex h-10 w-10 items-center justify-center rounded-full"
            :class="healthIconBg"
          >
            <svg v-if="healthLoading" class="animate-spin text-slate-400" width="18" height="18" viewBox="0 0 24 24" fill="none">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
              <path class="opacity-75 fill-current" d="M4 12a8 8 0 018-8v4a4 4 0 00-4 4H4z" />
            </svg>
            <IconCircleCheck v-else-if="health?.status === 'Healthy'" color="var(--color-green-500)" />
            <IconAlertCircle v-else-if="health?.status === 'Degraded'" color="var(--color-amber-500)" />
            <IconXboxX v-else-if="health?.status === 'Unhealthy'" color="var(--color-rose-500)" />
            <span v-else class="text-slate-400 text-xs">?</span>
          </div>
        </div>

        <!-- Loading state -->
        <div v-if="healthLoading" class="flex items-center gap-2">
          <span class="inline-block w-2 h-2 rounded-full bg-slate-300 dark:bg-slate-600 animate-pulse"></span>
          <span class="text-lg font-bold text-slate-400 dark:text-slate-500">Checking…</span>
        </div>

        <div v-else>
          <!-- Overall status + check counter -->
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <span class="inline-block w-2 h-2 rounded-full" :class="healthDotColor"></span>
              <span class="text-lg font-bold text-slate-900 dark:text-white">{{ health?.status ?? 'Unknown' }}</span>
            </div>
            <!-- healthy / total badge -->
            <span
              v-if="health && health.checks.length"
              class="text-xs font-semibold px-1.5 py-0.5 rounded-md"
              :class="healthBadgeClass"
            >{{ healthyCount }} / {{ health.checks.length }}</span>
          </div>

          <!-- Attention-only list: unhealthy + degraded checks always visible -->
          <ul v-if="problematicChecks.length" class="mt-3 space-y-1.5">
            <li
              v-for="check in problematicChecks"
              :key="check.name"
              class="text-xs bg-rose-50 dark:bg-rose-500/10 rounded-md px-2 py-1"
            >
              <div class="flex items-center gap-2">
                <span class="inline-block w-1.5 h-1.5 rounded-full shrink-0" :class="checkDotColor(check.status)"></span>
                <span class="text-slate-700 dark:text-slate-300 truncate flex-1 min-w-0">{{ check.name }}</span>
                <span class="font-semibold shrink-0" :class="checkTextColor(check.status)">{{ check.status }}</span>
              </div>
              <!-- exception / description -->
              <div v-if="check.exception || check.description" class="ml-3.5 mt-0.5 text-rose-600 dark:text-rose-400 truncate">
                {{ check.exception || check.description }}
              </div>
              <!-- per-store sub-list -->
              <ul v-if="getChunkStoreData(check)" class="ml-3.5 mt-1 space-y-0.5">
                <li
                  v-for="store in getChunkStoreData(check).stores"
                  :key="store.storeId"
                  class="flex items-center gap-1.5"
                >
                  <span class="inline-block w-1 h-1 rounded-full shrink-0" :class="checkDotColor(store.status)"></span>
                  <span class="font-mono text-slate-600 dark:text-slate-400 truncate flex-1 min-w-0" :title="store.storeId">…{{ store.storeName.slice(-8) }}</span>
                  <span class="shrink-0" :class="checkTextColor(store.status)">{{ store.status }}</span>
                  <span v-if="store.error" class="ml-1 text-rose-500 truncate max-w-24" :title="store.error">{{ store.error }}</span>
                  <span class="ml-1 text-slate-400 dark:text-slate-500 shrink-0">{{ formatBytes(store.totalBytes - store.freeBytes) }} / {{ formatBytes(store.totalBytes) }}</span>
                </li>
              </ul>
            </li>
          </ul>

          <!-- All-healthy summary or expanded list -->
          <div v-if="health && health.checks.length" class="mt-3">
            <div v-if="!showAllChecks && !problematicChecks.length" class="text-xs text-slate-500 dark:text-slate-400">
              All {{ health.checks.length }} checks passing
            </div>

            <!-- Expanded list -->
            <ul v-if="showAllChecks" class="space-y-1.5 max-h-56 overflow-y-auto pr-1">
              <li
                v-for="check in health.checks"
                :key="check.name"
                class="text-xs"
              >
                <div class="flex items-center gap-2">
                  <span class="inline-block w-1.5 h-1.5 rounded-full shrink-0" :class="checkDotColor(check.status)"></span>
                  <span class="text-slate-600 dark:text-slate-300 truncate flex-1 min-w-0">{{ check.name }}</span>
                  <span class="ml-auto font-medium shrink-0" :class="checkTextColor(check.status)">{{ check.status }}</span>
                </div>
                <!-- exception / description -->
                <div v-if="check.exception || check.description" class="ml-3.5 mt-0.5 text-slate-500 dark:text-slate-400 truncate">
                  {{ check.exception || check.description }}
                </div>
                <!-- per-store sub-list -->
                <ul v-if="getChunkStoreData(check)" class="ml-3.5 mt-1 space-y-0.5">
                  <li
                    v-for="store in getChunkStoreData(check).stores"
                    :key="store.storeId"
                    class="flex items-center gap-1.5 text-slate-500 dark:text-slate-400"
                  >
                    <span class="inline-block w-1 h-1 rounded-full shrink-0" :class="checkDotColor(store.status)"></span>
                    <span class="font-mono truncate flex-1 min-w-0" :title="store.storeName">{{ store.storeName }}</span>
                    <span class="shrink-0" :class="checkTextColor(store.status)">{{ store.status }}</span>
                    <span v-if="store.error" class="ml-1 text-rose-500 truncate max-w-24" :title="store.error">{{ store.error }}</span>
                    <span class="ml-1 shrink-0">{{ formatBytes(store.totalBytes - store.freeBytes) }} / {{ formatBytes(store.totalBytes) }}</span>
                  </li>
                </ul>
              </li>
            </ul>

            <!-- Toggle -->
            <button
              class="mt-2 text-xs font-semibold text-[#7C86FF] transition hover:text-[#6974ff]"
              @click="showAllChecks = !showAllChecks"
            >
              {{ showAllChecks ? 'Hide checks' : `Show all ${health.checks.length} checks` }}
            </button>
          </div>
        </div>
      </div>
    </div>

  </div>

  <!-- Quick actions -->
  <div class="rounded-[28px] border border-slate-200 bg-white shadow-sm dark:border-white/5 dark:bg-[#0C112D] mb-6">
    <div class="px-5 py-4 border-b border-slate-100 dark:border-white/5">
      <h2 class="font-semibold text-slate-900 dark:text-white">Quick Actions</h2>
    </div>
    <div class="p-5">
      <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <router-link
          to="/instance/tenants"
          class="flex items-center gap-3 p-4 rounded-2xl border border-slate-200 dark:border-white/5 hover:border-[#7C86FF]/30 dark:hover:border-[#7C86FF]/20 hover:bg-[#7C86FF]/5 dark:hover:bg-[#7C86FF]/5 transition group"
        >
          <div class="w-9 h-9 rounded-full bg-[#7C86FF]/10 flex items-center justify-center shrink-0 group-hover:bg-[#7C86FF]/20 transition">
            <IconBuildingSkyscraper class="text-[#7C86FF] w-5 h-5" />
          </div>
          <div>
            <div class="text-sm font-medium text-slate-900 dark:text-white">Manage Tenants</div>
            <div class="text-xs text-slate-500 dark:text-slate-400">Create and configure tenants</div>
          </div>
        </router-link>

        <router-link
          to="/instance/users"
          class="flex items-center gap-3 p-4 rounded-2xl border border-slate-200 dark:border-white/5 hover:border-sky-300 dark:hover:border-sky-500/20 hover:bg-sky-50 dark:hover:bg-sky-500/5 transition group"
        >
          <div class="w-9 h-9 rounded-full bg-sky-500/10 flex items-center justify-center shrink-0 group-hover:bg-sky-500/20 transition">
            <IconUser class="text-sky-500 w-5 h-5" />
          </div>
          <div>
            <div class="text-sm font-medium text-slate-900 dark:text-white">Manage Users</div>
            <div class="text-xs text-slate-500 dark:text-slate-400">View and manage all users</div>
          </div>
        </router-link>

        <router-link
          to="/instance/settings"
          class="flex items-center gap-3 p-4 rounded-2xl border border-slate-200 dark:border-white/5 hover:border-amber-300 dark:hover:border-amber-500/20 hover:bg-amber-50 dark:hover:bg-amber-500/5 transition group"
        >
          <div class="w-9 h-9 rounded-full bg-amber-500/10 flex items-center justify-center shrink-0 group-hover:bg-amber-500/20 transition">
            <IconAdjustmentsHorizontal class="text-amber-500 w-5 h-5" />
          </div>
          <div>
            <div class="text-sm font-medium text-slate-900 dark:text-white">Instance Settings</div>
            <div class="text-xs text-slate-500 dark:text-slate-400">Configure global settings</div>
          </div>
        </router-link>
      </div>
    </div>
  </div>

  <!-- Instance-wide Metrics -->
  <div class="mb-6">
    <div class="flex items-center justify-between mb-4">
      <h2 class="font-semibold text-slate-900 dark:text-white">Instance Metrics</h2>
      <span class="text-xs text-slate-400 dark:text-slate-500 italic">Updated periodically</span>
    </div>
    <div class="grid grid-cols-12 gap-5">

      <!-- Total Storage Used -->
      <div class="col-span-full sm:col-span-6 xl:col-span-4 rounded-[28px] border border-slate-200 bg-white shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
        <div class="px-5 py-4 border-b border-slate-100 dark:border-white/5 flex items-center gap-2">
          <div class="w-7 h-7 rounded-full bg-indigo-500/10 flex items-center justify-center shrink-0">
            <IconDatabase class="text-indigo-500 w-4 h-4" />
          </div>
          <h3 class="text-sm font-semibold text-slate-700 dark:text-slate-300">Total Storage Used</h3>
        </div>
        <div class="p-5">
          <div class="flex items-start gap-3 mb-3">
            <div class="flex-1 min-w-0">
              <div class="flex items-end gap-2 mb-1.5">
                <div class="text-3xl font-bold text-slate-900 dark:text-white">{{ instanceMetrics.storage.used }}</div>
                <div
                  class="flex items-center gap-0.5 text-sm font-semibold mb-0.5"
                  :class="instanceMetrics.storage.trend === 'up' ? 'text-rose-500' : 'text-green-500'"
                >
                  <IconArrowNarrowUp v-if="instanceMetrics.storage.trend === 'up'" class="w-4 h-4 shrink-0" />
                  <IconArrowNarrowDown v-else class="w-4 h-4 shrink-0" />
                  {{ instanceMetrics.storage.trendPercent }}
                </div>
              </div>
              <div class="text-xs text-slate-500 dark:text-slate-400">
                Last 24h: <span class="font-medium text-slate-700 dark:text-slate-300">{{ instanceMetrics.storage.change24h }}</span>
              </div>
            </div>
            <div class="w-28 shrink-0">
              <SparklineChart :data="instanceMetrics.storage.sparkline" color="#6366f1" :height="44" />
              <div class="flex justify-between text-xs text-slate-400 dark:text-slate-500 mt-0.5">
                <span>30d</span>
                <span>Now</span>
              </div>
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3 pt-3 border-t border-slate-100 dark:border-white/5">
            <div>
              <div class="text-xs text-slate-400 dark:text-slate-500 uppercase tracking-wide mb-0.5">Raw (before dedup)</div>
              <div class="text-sm font-semibold text-slate-700 dark:text-slate-300">{{ instanceMetrics.storage.raw }}</div>
            </div>
            <div>
              <div class="text-xs text-slate-400 dark:text-slate-500 uppercase tracking-wide mb-0.5">Dedup Ratio</div>
              <div class="flex items-baseline gap-1">
                <span class="text-sm font-semibold text-indigo-600 dark:text-indigo-400">{{ instanceMetrics.storage.dedupRatio }}</span>
                <span class="text-xs text-slate-400 dark:text-slate-500">reduction</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Total Releases -->
      <div class="col-span-full sm:col-span-6 xl:col-span-4 rounded-[28px] border border-slate-200 bg-white shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
        <div class="px-5 py-4 border-b border-slate-100 dark:border-white/5 flex items-center gap-2">
          <div class="w-7 h-7 rounded-full bg-emerald-500/10 flex items-center justify-center shrink-0">
            <IconPackage class="text-emerald-500 w-4 h-4" />
          </div>
          <h3 class="text-sm font-semibold text-slate-700 dark:text-slate-300">Total Releases</h3>
        </div>
        <div class="p-5">
          <div class="flex items-start gap-3 mb-3">
            <div class="flex-1 min-w-0">
              <div class="flex items-end gap-2 mb-1.5">
                <div class="text-3xl font-bold text-slate-900 dark:text-white">{{ instanceMetrics.releases.total }}</div>
                <div
                  class="flex items-center gap-0.5 text-sm font-semibold mb-0.5"
                  :class="instanceMetrics.releases.trend === 'up' ? 'text-rose-500' : 'text-green-500'"
                >
                  <IconArrowNarrowUp v-if="instanceMetrics.releases.trend === 'up'" class="w-4 h-4 shrink-0" />
                  <IconArrowNarrowDown v-else class="w-4 h-4 shrink-0" />
                  {{ instanceMetrics.releases.trendPercent }}
                </div>
              </div>
              <div class="text-xs text-slate-500 dark:text-slate-400">
                Last 24h: <span class="font-medium text-slate-700 dark:text-slate-300">{{ instanceMetrics.releases.change24h }}</span>
              </div>
            </div>
            <div class="w-28 shrink-0">
              <SparklineChart :data="instanceMetrics.releases.sparkline" color="#6366f1" :height="44" />
              <div class="flex justify-between text-xs text-slate-400 dark:text-slate-500 mt-0.5">
                <span>30d</span>
                <span>Now</span>
              </div>
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3 pt-3 border-t border-slate-100 dark:border-white/5">
            <div>
              <div class="text-xs text-slate-400 dark:text-slate-500 uppercase tracking-wide mb-0.5">Chunks used</div>
              <div class="text-sm font-semibold text-slate-700 dark:text-slate-300">{{ instanceMetrics.chunks.total }}</div>
            </div>
            <div>
              <div class="text-xs text-slate-400 dark:text-slate-500 uppercase tracking-wide mb-0.5">Chunk stores</div>
              <div class="flex items-baseline gap-1">
                <span class="text-sm font-semibold text-indigo-600 dark:text-indigo-400">{{ instanceMetrics.chunkStores.total }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Ingress / Egress -->
      <div class="col-span-full xl:col-span-4 rounded-[28px] border border-slate-200 bg-white shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
        <div class="px-5 py-4 border-b border-slate-100 dark:border-white/5 flex items-center gap-2">
          <div class="w-7 h-7 rounded-full bg-cyan-500/10 flex items-center justify-center shrink-0">
            <IconArrowsTransferDown class="text-cyan-500 w-4 h-4" />
          </div>
          <h3 class="text-sm font-semibold text-slate-700 dark:text-slate-300">Ingress / Egress</h3>
        </div>
        <div class="p-5 grid grid-cols-2 divide-x divide-slate-100 dark:divide-white/5">
          <!-- Ingress -->
          <div class="pr-4">
            <div class="flex items-center gap-1.5 mb-2">
              <IconArrowNarrowDown class="text-sky-500 w-4 h-4 shrink-0" />
              <span class="text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wide">Ingress</span>
            </div>
            <div class="text-xl font-bold text-slate-900 dark:text-white mb-0.5">{{ instanceMetrics.ingress.processed }}</div>
            <div class="text-xs text-slate-400 dark:text-slate-500 mb-3">After deduplication</div>
            <div class="pt-2 border-t border-slate-100 dark:border-white/5 space-y-1.5">
              <div class="flex justify-between text-xs">
                <span class="text-slate-400 dark:text-slate-500">Raw</span>
                <span class="font-medium text-slate-600 dark:text-slate-300">{{ instanceMetrics.ingress.raw }}</span>
              </div>
              <div class="flex justify-between text-xs">
                <span class="text-slate-400 dark:text-slate-500">Dedup</span>
                <span class="font-semibold text-sky-600 dark:text-sky-400">{{ instanceMetrics.ingress.dedupRatio }}</span>
              </div>
            </div>
          </div>
          <!-- Egress -->
          <div class="pl-4">
            <div class="flex items-center gap-1.5 mb-2">
              <IconArrowNarrowUp class="text-[#7C86FF] w-4 h-4 shrink-0" />
              <span class="text-xs font-semibold text-slate-500 dark:text-slate-400 uppercase tracking-wide">Egress</span>
            </div>
            <div class="text-xl font-bold text-slate-900 dark:text-white mb-0.5">{{ instanceMetrics.egress.processed }}</div>
            <div class="text-xs text-slate-400 dark:text-slate-500 mb-3">After deduplication</div>
            <div class="pt-2 border-t border-slate-100 dark:border-white/5 space-y-1.5">
              <div class="flex justify-between text-xs">
                <span class="text-slate-400 dark:text-slate-500">Raw</span>
                <span class="font-medium text-slate-600 dark:text-slate-300">{{ instanceMetrics.egress.raw }}</span>
              </div>
              <div class="flex justify-between text-xs">
                <span class="text-slate-400 dark:text-slate-500">Dedup</span>
                <span class="font-semibold text-[#7C86FF] dark:text-[#7C86FF]">{{ instanceMetrics.egress.dedupRatio }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Throughput -->
      <div class="col-span-full rounded-[28px] border border-slate-200 bg-white shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
        <div class="px-5 py-4 border-b border-slate-100 dark:border-white/5 flex items-center gap-2">
          <div class="w-7 h-7 rounded-full bg-orange-500/10 flex items-center justify-center shrink-0">
            <IconGauge class="text-orange-500 w-4 h-4" />
          </div>
          <h3 class="text-sm font-semibold text-slate-700 dark:text-slate-300">Throughput</h3>
          <span class="ml-auto text-xs text-slate-400 dark:text-slate-500 italic">Real-time operational performance</span>
        </div>
        <div class="p-5">
          <div class="grid grid-cols-2 sm:grid-cols-4 gap-6">
            <!-- Upload Speed -->
            <div class="flex items-center gap-3">
              <div class="w-10 h-10 rounded-full bg-sky-500/10 flex items-center justify-center shrink-0">
                <IconCloudUpload class="text-sky-500 w-5 h-5" />
              </div>
              <div>
                <div class="text-xl font-bold text-slate-900 dark:text-white">{{ instanceMetrics.throughput.uploadSpeed }}</div>
                <div class="text-xs text-slate-500 dark:text-slate-400">Upload speed</div>
              </div>
            </div>
            <!-- Download Speed -->
            <div class="flex items-center gap-3">
              <div class="w-10 h-10 rounded-full bg-[#7C86FF]/10 flex items-center justify-center shrink-0">
                <IconCloudDownload class="text-[#7C86FF] w-5 h-5" />
              </div>
              <div>
                <div class="text-xl font-bold text-slate-900 dark:text-white">{{ instanceMetrics.throughput.downloadSpeed }}</div>
                <div class="text-xs text-slate-500 dark:text-slate-400">Download speed</div>
              </div>
            </div>
            <!-- Chunk Writes/sec -->
            <div class="flex items-center gap-3">
              <div class="w-10 h-10 rounded-full bg-amber-500/10 flex items-center justify-center shrink-0">
                <IconArrowNarrowUp class="text-amber-500 w-5 h-5" />
              </div>
              <div>
                <div class="text-xl font-bold text-slate-900 dark:text-white">{{ instanceMetrics.throughput.chunkWritesPerSec }}</div>
                <div class="text-xs text-slate-500 dark:text-slate-400">Chunk writes/sec</div>
              </div>
            </div>
            <!-- Chunk Reads/sec -->
            <div class="flex items-center gap-3">
              <div class="w-10 h-10 rounded-full bg-emerald-500/10 flex items-center justify-center shrink-0">
                <IconArrowNarrowDown class="text-emerald-500 w-5 h-5" />
              </div>
              <div>
                <div class="text-xl font-bold text-slate-900 dark:text-white">{{ instanceMetrics.throughput.chunkReadsPerSec }}</div>
                <div class="text-xs text-slate-500 dark:text-slate-400">Chunk reads/sec</div>
              </div>
            </div>
          </div>
        </div>
      </div>

    </div>
  </div>

  <!-- Admin info banner -->
  <div class="rounded-[28px] border border-[#7C86FF]/20 bg-[#7C86FF]/5 dark:border-[#7C86FF]/20 dark:bg-[#7C86FF]/5 p-5 flex items-start gap-4">
    <div class="shrink-0 mt-0.5">
      <IconAlertCircleFilled class="text-[#7C86FF] w-5 h-5" />
    </div>
    <div>
      <h3 class="text-sm font-semibold text-[#7C86FF] dark:text-[#9BA3FF] mb-1">
        You are logged in as an Instance Administrator
      </h3>
      <p class="text-sm text-slate-600 dark:text-slate-400">
        This area provides full administrative control over the instance, including all tenants,
        users, and system-wide settings. Actions taken here affect the entire platform.
      </p>
    </div>
  </div>
</template>

<script>
import { ref, computed, onMounted, onUnmounted } from 'vue'
import { fetchHealth } from '@/api/health'
import { fetchInstanceStats } from '@/api/instance'
import { IconCircleCheck, IconAlertCircle, IconAlertCircleFilled, IconXboxX, IconUser, IconBuildingSkyscraper, IconAdjustmentsHorizontal, IconDatabase, IconArrowNarrowUp, IconArrowNarrowDown, IconPackage, IconArrowsTransferDown, IconGitBranch, IconGauge, IconCloudUpload, IconCloudDownload, IconObjectScan } from '@tabler/icons-vue';
import SparklineChart from '@/components/SparklineChart.vue'

const HEALTH_POLL_INTERVAL_MS = 30_000

export default {
  name: 'InstanceDashboard',
  components: {
    SparklineChart,
    IconCircleCheck,
    IconAlertCircle,
    IconAlertCircleFilled,
    IconXboxX,
    IconUser,
    IconBuildingSkyscraper,
    IconAdjustmentsHorizontal,
    IconDatabase,
    IconArrowNarrowUp,
    IconArrowNarrowDown,
    IconPackage,
    IconArrowsTransferDown,
    IconGitBranch,
    IconGauge,
    IconCloudUpload,
    IconCloudDownload,
    IconObjectScan,
  },
  setup() {
    // Dummy metrics — replace values with server fetch once API is available
    const instanceMetrics = ref({
      storage: {
        used: '12.3 TB',
        raw: '20.1 TB',
        dedupRatio: '1.63×',
        change24h: '+120 GB',
        trend: 'up',
        trendPercent: '+2.4%',
        sparkline: [9.2, 9.5, 9.8, 10.1, 10.3, 10.6, 10.8, 11.0, 11.2, 11.4, 11.6, 11.7, 11.9, 12.0, 12.1, 12.3],
      },
      releases: {
        total: 84231,
        change24h: '+120',
        trend: 'up',
        trendPercent: '+1.4%',
        sparkline: [58000, 60500, 62000, 63800, 65200, 67000, 68500, 70000, 71200, 72800, 74000, 75500, 77000, 79000, 81500, 84231],
      },
      chunks: {
        total: 5_230_000,
      },
      chunkStores: {
        total: 12,
      },
      ingress: {
        processed: '2.1 TB',
        raw: '3.8 TB',
        dedupRatio: '1.81×',
      },
      egress: {
        processed: '1.8 TB',
        raw: '3.2 TB',
        dedupRatio: '1.78×',
      },
      repos: {
        change24h: '+14',
        trendPercent: '+2.1%',
        trend: 'up',
      },
      throughput: {
        uploadSpeed: '310 MB/s',
        downloadSpeed: '540 MB/s',
        chunkWritesPerSec: '18k',
        chunkReadsPerSec: '41k',
      },
    })

    const health = ref(null)
    const healthLoading = ref(true)
    const stats = ref(null)
    let pollTimer = null

    async function loadStats() {
      try {
        stats.value = await fetchInstanceStats()
      } catch {
        stats.value = null
      }
    }

    async function loadHealth() {
      try {
        health.value = await fetchHealth()
      } catch {
        health.value = { status: 'Unhealthy', checks: [] }
      } finally {
        healthLoading.value = false
      }
    }

    onMounted(() => {
      loadHealth()
      loadStats()
      pollTimer = setInterval(loadHealth, HEALTH_POLL_INTERVAL_MS)
    })

    onUnmounted(() => {
      clearInterval(pollTimer)
    })

    const healthIconBg = computed(() => {
      if (healthLoading.value) return 'bg-slate-100 dark:bg-slate-700'
      switch (health.value?.status) {
        case 'Healthy':   return 'bg-green-500/10'
        case 'Degraded':  return 'bg-amber-500/10'
        default:          return 'bg-rose-500/10'
      }
    })

    const healthDotColor = computed(() => {
      switch (health.value?.status) {
        case 'Healthy':   return 'bg-green-500'
        case 'Degraded':  return 'bg-amber-500'
        default:          return 'bg-rose-500'
      }
    })

    const healthSummary = computed(() => {
      if (!health.value) return ''
      const checks = health.value.checks
      if (!checks.length) return 'No checks reported'
      const unhealthy = checks.filter(c => c.status === 'Unhealthy').length
      const degraded  = checks.filter(c => c.status === 'Degraded').length
      if (unhealthy === 0 && degraded === 0) return `${checks.length} check${checks.length !== 1 ? 's' : ''} passing`
      const parts = []
      if (unhealthy) parts.push(`${unhealthy} unhealthy`)
      if (degraded)  parts.push(`${degraded} degraded`)
      return parts.join(', ')
    })

    function checkDotColor(status) {
      switch (status) {
        case 'Healthy':   return 'bg-green-500'
        case 'Degraded':  return 'bg-amber-500'
        default:          return 'bg-rose-500'
      }
    }

    function checkTextColor(status) {
      switch (status) {
        case 'Healthy':   return 'text-green-600 dark:text-green-400'
        case 'Degraded':  return 'text-amber-600 dark:text-amber-400'
        default:          return 'text-rose-600 dark:text-rose-400'
      }
    }

    const showAllChecks = ref(false)

    const healthyCount = computed(() =>
      health.value?.checks.filter(c => c.status === 'Healthy').length ?? 0,
    )

    const problematicChecks = computed(() =>
      health.value?.checks.filter(c => c.status !== 'Healthy') ?? [],
    )

    const healthBadgeClass = computed(() => {
      if (!health.value) return ''
      const total = health.value.checks.length
      const healthy = healthyCount.value
      if (healthy === total) return 'bg-green-100 text-green-700 dark:bg-green-500/20 dark:text-green-400'
      if (healthy >= total / 2) return 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-400'
      return 'bg-rose-100 text-rose-700 dark:bg-rose-500/20 dark:text-rose-400'
    })

    function formatBytes(bytes) {
      if (!bytes || bytes === 0) return '0 B'
      const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']
      const i = Math.floor(Math.log(bytes) / Math.log(1024))
      return `${(bytes / Math.pow(1024, i)).toFixed(1)} ${units[i]}`
    }

    function getChunkStoreData(check) {
      const data = check?.data
      if (!data || !Array.isArray(data.stores)) return null
      return data
    }

    return {
      instanceMetrics,
      health,
      healthLoading,
      stats,
      healthIconBg,
      healthDotColor,
      healthSummary,
      checkDotColor,
      checkTextColor,
      showAllChecks,
      healthyCount,
      problematicChecks,
      healthBadgeClass,
      formatBytes,
      getChunkStoreData,
    }
  },
}
</script>