<template>
  <!-- Page header -->
  <PageHeader
    title="Instance Overview"
    description="System-wide administration and monitoring."
  />

  <!-- Stats cards -->
  <div class="mb-8 grid grid-cols-12 gap-5">

    <!-- Tenants card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3">
      <StatCard label="Tenants" :value="stats?.tenantCount ?? '—'" :icon="IconBuildingSkyscraper" tone="accent" hint="Total tenants">
        <template #footer>
          <router-link to="/instance/tenants" class="text-xs font-semibold text-accent transition hover:brightness-110">View all →</router-link>
        </template>
      </StatCard>
    </div>

    <!-- Users card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3">
      <StatCard label="Users" :value="stats?.userCount ?? '—'" :icon="IconUser" tone="accent" hint="Registered users">
        <template #footer>
          <router-link to="/instance/users" class="text-xs font-semibold text-accent transition hover:brightness-110">View all →</router-link>
        </template>
      </StatCard>
    </div>

    <!-- Repositories card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3">
      <StatCard label="Repositories" :value="stats?.repositoryCount ?? '—'" :icon="IconGitBranch" tone="success" hint="Total repositories" />
    </div>

    <!-- System Status card -->
    <div class="col-span-full sm:col-span-6 xl:col-span-3 rounded-card border border-hairline bg-card p-5 shadow-sm transition-colors">
      <div>
        <div class="mb-5 flex items-center justify-between">
          <div class="text-xs font-medium uppercase tracking-wide text-ink-subtle">Status</div>
          <div
            class="flex h-10 w-10 items-center justify-center rounded-xl"
            :class="healthIconBg"
          >
            <Spinner v-if="healthLoading" :size="18" color="var(--color-accent)" />
            <IconCircleCheck v-else-if="health?.status === 'Healthy'" color="var(--color-green-500)" />
            <IconAlertCircle v-else-if="health?.status === 'Degraded'" color="var(--color-amber-500)" />
            <IconXboxX v-else-if="health?.status === 'Unhealthy'" color="var(--color-rose-500)" />
            <span v-else class="text-ink-subtle text-xs">?</span>
          </div>
        </div>

        <!-- Loading state -->
        <div v-if="healthLoading" class="flex items-center gap-2">
          <span class="inline-block w-2 h-2 rounded-full bg-raised animate-pulse"></span>
          <span class="text-lg font-bold text-ink-subtle">Checking…</span>
        </div>

        <div v-else>
          <!-- Overall status + check counter -->
          <div class="flex items-center justify-between">
            <div class="flex items-center gap-2">
              <span class="inline-block w-2 h-2 rounded-full" :class="healthDotColor"></span>
              <span class="text-lg font-bold text-ink-strong">{{ health?.status ?? 'Unknown' }}</span>
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
              class="text-xs bg-danger-soft rounded-md px-2 py-1"
            >
              <div class="flex items-center gap-2">
                <span class="inline-block w-1.5 h-1.5 rounded-full shrink-0" :class="checkDotColor(check.status)"></span>
                <span class="text-ink-strong truncate flex-1 min-w-0">{{ check.name }}</span>
                <span class="font-semibold shrink-0" :class="checkTextColor(check.status)">{{ check.status }}</span>
              </div>
              <!-- exception / description -->
              <div v-if="check.exception || check.description" class="ml-3.5 mt-0.5 text-danger truncate">
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
                  <span class="font-mono text-ink-muted truncate flex-1 min-w-0" :title="store.storeId">…{{ store.storeName.slice(-8) }}</span>
                  <span class="shrink-0" :class="checkTextColor(store.status)">{{ store.status }}</span>
                  <span v-if="store.error" class="ml-1 text-danger truncate max-w-24" :title="store.error">{{ store.error }}</span>
                  <span class="ml-1 text-ink-subtle shrink-0">{{ formatBytes(store.totalBytes - store.freeBytes) }} / {{ formatBytes(store.totalBytes) }}</span>
                </li>
              </ul>
            </li>
          </ul>

          <!-- All-healthy summary or expanded list -->
          <div v-if="health && health.checks.length" class="mt-3">
            <div v-if="!showAllChecks && !problematicChecks.length" class="text-xs text-ink-muted">
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
                  <span class="text-ink-muted truncate flex-1 min-w-0">{{ check.name }}</span>
                  <span class="ml-auto font-medium shrink-0" :class="checkTextColor(check.status)">{{ check.status }}</span>
                </div>
                <!-- exception / description -->
                <div v-if="check.exception || check.description" class="ml-3.5 mt-0.5 text-ink-subtle truncate">
                  {{ check.exception || check.description }}
                </div>
                <!-- per-store sub-list -->
                <ul v-if="getChunkStoreData(check)" class="ml-3.5 mt-1 space-y-0.5">
                  <li
                    v-for="store in getChunkStoreData(check).stores"
                    :key="store.storeId"
                    class="flex items-center gap-1.5 text-ink-subtle"
                  >
                    <span class="inline-block w-1 h-1 rounded-full shrink-0" :class="checkDotColor(store.status)"></span>
                    <span class="font-mono truncate flex-1 min-w-0" :title="store.storeName">{{ store.storeName }}</span>
                    <span class="shrink-0" :class="checkTextColor(store.status)">{{ store.status }}</span>
                    <span v-if="store.error" class="ml-1 text-danger truncate max-w-24" :title="store.error">{{ store.error }}</span>
                    <span class="ml-1 shrink-0">{{ formatBytes(store.totalBytes - store.freeBytes) }} / {{ formatBytes(store.totalBytes) }}</span>
                  </li>
                </ul>
              </li>
            </ul>

            <!-- Toggle -->
            <button
              class="mt-2 text-xs font-semibold text-accent transition hover:brightness-110"
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
  <DashboardCard title="Quick Actions" :icon="IconAdjustmentsHorizontal" class="mb-6">
    <div class="p-5">
      <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <router-link
          to="/instance/tenants"
          class="group flex items-center gap-3 rounded-card border border-hairline p-4 transition hover:border-accent/30 hover:bg-raised"
        >
          <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-accent-soft text-accent">
            <IconBuildingSkyscraper class="h-5 w-5" />
          </div>
          <div>
            <div class="text-sm font-medium text-ink-strong">Manage Tenants</div>
            <div class="text-xs text-ink-muted">Create and configure tenants</div>
          </div>
        </router-link>

        <router-link
          to="/instance/users"
          class="group flex items-center gap-3 rounded-card border border-hairline p-4 transition hover:border-accent/30 hover:bg-raised"
        >
          <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-accent-soft text-accent">
            <IconUser class="h-5 w-5" />
          </div>
          <div>
            <div class="text-sm font-medium text-ink-strong">Manage Users</div>
            <div class="text-xs text-ink-muted">View and manage all users</div>
          </div>
        </router-link>

        <router-link
          to="/instance/settings"
          class="group flex items-center gap-3 rounded-card border border-hairline p-4 transition hover:border-accent/30 hover:bg-raised"
        >
          <div class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl bg-warning-soft text-warning">
            <IconAdjustmentsHorizontal class="h-5 w-5" />
          </div>
          <div>
            <div class="text-sm font-medium text-ink-strong">Instance Settings</div>
            <div class="text-xs text-ink-muted">Configure global settings</div>
          </div>
        </router-link>
      </div>
    </div>
  </DashboardCard>

  <!-- Instance-wide Metrics -->
  <div class="mb-6">
    <div class="flex items-center justify-between mb-4">
      <h2 class="font-semibold text-ink-strong">Instance Metrics</h2>
      <span class="text-xs italic text-ink-subtle">Updated periodically</span>
    </div>
    <div class="grid grid-cols-12 gap-5">

      <!-- Total Storage Used -->
      <div class="col-span-full sm:col-span-6 xl:col-span-4 rounded-card border border-hairline bg-card shadow-sm">
        <div class="flex items-center gap-2 border-b border-hairline px-5 py-4">
          <div class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent">
            <IconDatabase class="h-4 w-4" />
          </div>
          <h3 class="text-sm font-semibold text-ink-strong">Total Storage Used</h3>
        </div>
        <div class="p-5">
          <div class="mb-3 flex items-start gap-3">
            <div class="min-w-0 flex-1">
              <div class="mb-1.5 flex items-end gap-2">
                <div class="text-3xl font-bold text-ink-strong">{{ instanceMetrics.storage.used }}</div>
                <div
                  class="mb-0.5 flex items-center gap-0.5 text-sm font-semibold"
                  :class="instanceMetrics.storage.trend === 'up' ? 'text-danger' : 'text-success'"
                >
                  <IconArrowNarrowUp v-if="instanceMetrics.storage.trend === 'up'" class="h-4 w-4 shrink-0" />
                  <IconArrowNarrowDown v-else class="h-4 w-4 shrink-0" />
                  {{ instanceMetrics.storage.trendPercent }}
                </div>
              </div>
              <div class="text-xs text-ink-muted">
                Last 24h: <span class="font-medium text-ink-strong">{{ instanceMetrics.storage.change24h }}</span>
              </div>
            </div>
            <div class="w-28 shrink-0">
              <SparklineChart :data="instanceMetrics.storage.sparkline" color="#6366f1" :height="44" />
              <div class="mt-0.5 flex justify-between text-xs text-ink-subtle">
                <span>30d</span>
                <span>Now</span>
              </div>
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3 border-t border-hairline pt-3">
            <div>
              <div class="mb-0.5 text-xs uppercase tracking-wide text-ink-subtle">Raw (before dedup)</div>
              <div class="text-sm font-semibold text-ink-strong">{{ instanceMetrics.storage.raw }}</div>
            </div>
            <div>
              <div class="mb-0.5 text-xs uppercase tracking-wide text-ink-subtle">Dedup Ratio</div>
              <div class="flex items-baseline gap-1">
                <span class="text-sm font-semibold text-accent">{{ instanceMetrics.storage.dedupRatio }}</span>
                <span class="text-xs text-ink-subtle">reduction</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Total Releases -->
      <div class="col-span-full sm:col-span-6 xl:col-span-4 rounded-card border border-hairline bg-card shadow-sm">
        <div class="flex items-center gap-2 border-b border-hairline px-5 py-4">
          <div class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-success-soft text-success">
            <IconPackage class="h-4 w-4" />
          </div>
          <h3 class="text-sm font-semibold text-ink-strong">Total Releases</h3>
        </div>
        <div class="p-5">
          <div class="mb-3 flex items-start gap-3">
            <div class="min-w-0 flex-1">
              <div class="mb-1.5 flex items-end gap-2">
                <div class="text-3xl font-bold text-ink-strong">{{ instanceMetrics.releases.total }}</div>
                <div
                  class="mb-0.5 flex items-center gap-0.5 text-sm font-semibold"
                  :class="instanceMetrics.releases.trend === 'up' ? 'text-danger' : 'text-success'"
                >
                  <IconArrowNarrowUp v-if="instanceMetrics.releases.trend === 'up'" class="h-4 w-4 shrink-0" />
                  <IconArrowNarrowDown v-else class="h-4 w-4 shrink-0" />
                  {{ instanceMetrics.releases.trendPercent }}
                </div>
              </div>
              <div class="text-xs text-ink-muted">
                Last 24h: <span class="font-medium text-ink-strong">{{ instanceMetrics.releases.change24h }}</span>
              </div>
            </div>
            <div class="w-28 shrink-0">
              <SparklineChart :data="instanceMetrics.releases.sparkline" color="#6366f1" :height="44" />
              <div class="mt-0.5 flex justify-between text-xs text-ink-subtle">
                <span>30d</span>
                <span>Now</span>
              </div>
            </div>
          </div>
          <div class="grid grid-cols-2 gap-3 border-t border-hairline pt-3">
            <div>
              <div class="mb-0.5 text-xs uppercase tracking-wide text-ink-subtle">Chunks used</div>
              <div class="text-sm font-semibold text-ink-strong">{{ instanceMetrics.chunks.total }}</div>
            </div>
            <div>
              <div class="mb-0.5 text-xs uppercase tracking-wide text-ink-subtle">Chunk stores</div>
              <div class="flex items-baseline gap-1">
                <span class="text-sm font-semibold text-accent">{{ instanceMetrics.chunkStores.total }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Ingress / Egress -->
      <div class="col-span-full xl:col-span-4 rounded-card border border-hairline bg-card shadow-sm">
        <div class="flex items-center gap-2 border-b border-hairline px-5 py-4">
          <div class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent">
            <IconArrowsTransferDown class="h-4 w-4" />
          </div>
          <h3 class="text-sm font-semibold text-ink-strong">Ingress / Egress</h3>
        </div>
        <div class="grid grid-cols-2 divide-x divide-hairline p-5">
          <!-- Ingress -->
          <div class="pr-4">
            <div class="mb-2 flex items-center gap-1.5">
              <IconArrowNarrowDown class="h-4 w-4 shrink-0 text-accent" />
              <span class="text-xs font-semibold uppercase tracking-wide text-ink-muted">Ingress</span>
            </div>
            <div class="mb-0.5 text-xl font-bold text-ink-strong">{{ instanceMetrics.ingress.processed }}</div>
            <div class="mb-3 text-xs text-ink-subtle">After deduplication</div>
            <div class="space-y-1.5 border-t border-hairline pt-2">
              <div class="flex justify-between text-xs">
                <span class="text-ink-subtle">Raw</span>
                <span class="font-medium text-ink-muted">{{ instanceMetrics.ingress.raw }}</span>
              </div>
              <div class="flex justify-between text-xs">
                <span class="text-ink-subtle">Dedup</span>
                <span class="font-semibold text-accent">{{ instanceMetrics.ingress.dedupRatio }}</span>
              </div>
            </div>
          </div>
          <!-- Egress -->
          <div class="pl-4">
            <div class="mb-2 flex items-center gap-1.5">
              <IconArrowNarrowUp class="h-4 w-4 shrink-0 text-accent" />
              <span class="text-xs font-semibold uppercase tracking-wide text-ink-muted">Egress</span>
            </div>
            <div class="mb-0.5 text-xl font-bold text-ink-strong">{{ instanceMetrics.egress.processed }}</div>
            <div class="mb-3 text-xs text-ink-subtle">After deduplication</div>
            <div class="space-y-1.5 border-t border-hairline pt-2">
              <div class="flex justify-between text-xs">
                <span class="text-ink-subtle">Raw</span>
                <span class="font-medium text-ink-muted">{{ instanceMetrics.egress.raw }}</span>
              </div>
              <div class="flex justify-between text-xs">
                <span class="text-ink-subtle">Dedup</span>
                <span class="font-semibold text-accent">{{ instanceMetrics.egress.dedupRatio }}</span>
              </div>
            </div>
          </div>
        </div>
      </div>

      <!-- Throughput -->
      <div class="col-span-full rounded-card border border-hairline bg-card shadow-sm">
        <div class="flex items-center gap-2 border-b border-hairline px-5 py-4">
          <div class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full bg-warning-soft text-warning">
            <IconGauge class="h-4 w-4" />
          </div>
          <h3 class="text-sm font-semibold text-ink-strong">Throughput</h3>
          <span class="ml-auto text-xs italic text-ink-subtle">Real-time operational performance</span>
        </div>
        <div class="p-5">
          <div class="grid grid-cols-2 gap-6 sm:grid-cols-4">
            <!-- Upload Speed -->
            <div class="flex items-center gap-3">
              <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent">
                <IconCloudUpload class="h-5 w-5" />
              </div>
              <div>
                <div class="text-xl font-bold text-ink-strong">{{ instanceMetrics.throughput.uploadSpeed }}</div>
                <div class="text-xs text-ink-muted">Upload speed</div>
              </div>
            </div>
            <!-- Download Speed -->
            <div class="flex items-center gap-3">
              <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent">
                <IconCloudDownload class="h-5 w-5" />
              </div>
              <div>
                <div class="text-xl font-bold text-ink-strong">{{ instanceMetrics.throughput.downloadSpeed }}</div>
                <div class="text-xs text-ink-muted">Download speed</div>
              </div>
            </div>
            <!-- Chunk Writes/sec -->
            <div class="flex items-center gap-3">
              <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-warning-soft text-warning">
                <IconArrowNarrowUp class="h-5 w-5" />
              </div>
              <div>
                <div class="text-xl font-bold text-ink-strong">{{ instanceMetrics.throughput.chunkWritesPerSec }}</div>
                <div class="text-xs text-ink-muted">Chunk writes/sec</div>
              </div>
            </div>
            <!-- Chunk Reads/sec -->
            <div class="flex items-center gap-3">
              <div class="flex h-10 w-10 shrink-0 items-center justify-center rounded-full bg-success-soft text-success">
                <IconArrowNarrowDown class="h-5 w-5" />
              </div>
              <div>
                <div class="text-xl font-bold text-ink-strong">{{ instanceMetrics.throughput.chunkReadsPerSec }}</div>
                <div class="text-xs text-ink-muted">Chunk reads/sec</div>
              </div>
            </div>
          </div>
        </div>
      </div>

    </div>
  </div>

  <!-- Admin info banner -->
  <div class="flex items-start gap-4 rounded-card border border-accent/25 bg-accent-soft p-5">
    <div class="mt-0.5 shrink-0">
      <IconAlertCircleFilled class="h-5 w-5 text-accent" />
    </div>
    <div>
      <h3 class="mb-1 text-sm font-semibold text-accent">
        You are logged in as an Instance Administrator
      </h3>
      <p class="text-sm text-ink-muted">
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
import { PageHeader, StatCard } from '@/shared/components/ui'
import DashboardCard from '@/shared/components/data-display/DashboardCard.vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const HEALTH_POLL_INTERVAL_MS = 30_000

export default {
  name: 'InstanceDashboard',
  components: {
    SparklineChart,
    PageHeader,
    StatCard,
    DashboardCard,
    Spinner,
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
      if (healthLoading.value) return 'bg-raised'
      switch (health.value?.status) {
        case 'Healthy':   return 'bg-success-soft'
        case 'Degraded':  return 'bg-warning-soft'
        default:          return 'bg-danger-soft'
      }
    })

    const healthDotColor = computed(() => {
      switch (health.value?.status) {
        case 'Healthy':   return 'bg-success'
        case 'Degraded':  return 'bg-warning'
        default:          return 'bg-danger'
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
        case 'Healthy':   return 'bg-success'
        case 'Degraded':  return 'bg-warning'
        default:          return 'bg-danger'
      }
    }

    function checkTextColor(status) {
      switch (status) {
        case 'Healthy':   return 'text-success'
        case 'Degraded':  return 'text-warning'
        default:          return 'text-danger'
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
      if (healthy === total) return 'bg-success-soft text-success'
      if (healthy >= total / 2) return 'bg-warning-soft text-warning'
      return 'bg-danger-soft text-danger'
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