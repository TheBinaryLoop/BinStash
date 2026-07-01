<template>
  <div class="space-y-6">
    <Breadcrumbs
      v-if="!loading && release"
      :items="[
        { label: 'Repositories', to: `/t/${tenantId}/repositories` },
        { label: repoName, to: `/t/${tenantId}/repositories/${repoId}` },
        { label: `Release ${release.version}` },
      ]"
    />

    <div v-if="loading" class="flex justify-center py-24">
      <Spinner :size="32" color="var(--color-accent)" />
    </div>

    <div v-else-if="error" class="rounded-card border border-danger/20 bg-danger-soft px-5 py-4 text-sm text-danger">
      {{ error }}
    </div>

    <template v-else-if="release">
      <BaseCard accent>
        <div class="flex flex-col gap-5 xl:flex-row xl:items-start xl:justify-between">
          <div class="flex min-w-0 items-start gap-3">
            <div class="flex size-12 shrink-0 items-center justify-center rounded-full bg-accent-soft text-accent">
              <IconPackage class="size-6" />
            </div>
            <div class="min-w-0">
              <div class="flex flex-wrap items-center gap-3">
                <h1 class="text-2xl font-bold text-ink-strong">{{ release.version }}</h1>
                <BaseBadge variant="outline" :icon="IconCalendar">Published {{ fmtDateFull(release.createdAt) }}</BaseBadge>
              </div>
              <div class="mt-2 flex flex-wrap items-center gap-3">
                <span class="text-sm font-medium text-ink-muted">Release details</span>
                <span class="inline-flex items-center gap-2 rounded-full px-3 py-1 text-sm font-semibold"
                  :class="scoreBadgeClasses(releaseScore)">
                  Score {{ releaseScoreDisplay }}
                  <span class="text-xs font-medium opacity-80">{{ releaseScoreLabel }}</span>
                </span>
              </div>
              <p class="mt-2 max-w-3xl text-sm leading-6 text-ink-muted">
                {{ release.notes || 'A versioned package snapshot stored with content-defined chunking and deduplication.' }}
              </p>
            </div>
          </div>

          <BaseButton class="self-start" :icon="IconDownload" @click="downloadOpen = true">Download</BaseButton>
        </div>
      </BaseCard>

      <Tabs :tabs="detailTabs" v-model="activeTab" />

      <section v-if="activeTab === 'overview'" class="grid grid-cols-1 gap-6 xl:grid-cols-3">
        <div class="xl:col-span-2 space-y-6">
          <BaseCard>
            <template #header>
              <h2 class="flex items-center gap-2 font-semibold text-ink-strong">
                <IconFileText class="w-4 h-4 text-ink-muted" /> Release Notes
              </h2>
            </template>
            <template #actions>
              <BaseBadge tone="accent">Primary summary</BaseBadge>
            </template>
            <p v-if="release.notes" class="whitespace-pre-wrap text-sm leading-relaxed text-ink-muted">{{ release.notes }}</p>
            <p v-else class="text-sm text-ink-subtle">No release notes were provided for this version.</p>
          </BaseCard>

          <BaseCard v-if="metrics" :padded="false">
            <template #header>
              <h2 class="font-semibold text-ink-strong">Key outcomes</h2>
              <p class="text-xs text-ink-subtle">The most important quality signals for this release.</p>
            </template>
            <div class="grid grid-cols-1 gap-4 p-5 sm:grid-cols-2">
              <MetricCard
                v-for="metric in topMetrics"
                :key="metric.key"
                :label="metric.label"
                :value="metric.value"
                :description="metric.description"
                :tone="metric.tone"
                :info="metric.help"
              />
            </div>
          </BaseCard>
        </div>

        <div class="space-y-6">
          <BaseCard :padded="false">
            <template #header>
              <h2 class="text-lg font-semibold text-ink-strong">Release details</h2>
            </template>
            <dl class="divide-y divide-hairline">
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-ink-subtle">Version</dt>
                <dd class="mt-1 font-mono text-sm font-semibold text-ink-strong">{{ release.version }}</dd>
              </div>
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-ink-subtle">Repository</dt>
                <dd class="mt-1 text-sm font-semibold text-ink-strong">{{ repoName }}</dd>
              </div>
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-ink-subtle">Published</dt>
                <dd class="mt-1 text-sm font-semibold text-ink-strong">{{ fmtDateFull(release.createdAt) }}</dd>
              </div>
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-ink-subtle">Release ID</dt>
                <dd class="mt-1 break-all font-mono text-xs text-ink-muted">{{ releaseId }}</dd>
              </div>
            </dl>
          </BaseCard>

          <BaseCard>
            <template #header>
              <h2 class="text-lg font-semibold text-ink-strong">Quick assessment</h2>
            </template>
            <div class="space-y-4">
              <MetricCard
                v-for="item in assessments"
                :key="item.label"
                :label="item.label"
                :value="item.summary"
                :description="item.detail"
                :tone="item.tone"
              />
            </div>
          </BaseCard>
        </div>
      </section>

      <section v-else-if="activeTab === 'metrics'">
        <div v-if="metrics" class="grid grid-cols-1 gap-6 xl:grid-cols-3">
          <div class="xl:col-span-2 space-y-6">
            <BaseCard>
              <template #header>
                <div>
                  <h2 class="text-lg font-semibold text-ink-strong">Storage impact</h2>
                  <p class="mt-1 text-sm text-ink-muted">How much logical content this release represents versus how much truly new storage it introduced.</p>
                </div>
              </template>
              <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
                <MetricCard
                  v-for="metric in storageMetrics"
                  :key="metric.key"
                  :label="metric.label"
                  :value="metric.value"
                  :description="metric.description"
                  :tone="metric.tone"
                  :info="metric.help"
                />
              </div>
            </BaseCard>

            <BaseCard>
              <template #header>
                <div>
                  <h2 class="text-lg font-semibold text-ink-strong">Deduplication &amp; compression</h2>
                  <p class="mt-1 text-sm text-ink-muted">These metrics show the quality of reuse and compression achieved for this release.</p>
                </div>
              </template>
              <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-3">
                <MetricCard
                  v-for="metric in efficiencyMetrics"
                  :key="metric.key"
                  :label="metric.label"
                  :value="metric.value"
                  :description="metric.description"
                  :tone="metric.tone"
                  :info="metric.help"
                />
              </div>
            </BaseCard>

            <BaseCard>
              <template #header>
                <div>
                  <h2 class="text-lg font-semibold text-ink-strong">Release composition</h2>
                  <p class="mt-1 text-sm text-ink-muted">Counts and metadata that describe what is inside the release and how it maps to chunked storage.</p>
                </div>
              </template>
              <div class="grid grid-cols-1 gap-4 md:grid-cols-2 xl:grid-cols-4">
                <MetricCard
                  v-for="metric in compositionMetrics"
                  :key="metric.key"
                  :label="metric.label"
                  :value="metric.value"
                  :description="metric.description"
                  tone="neutral"
                  :info="metric.help"
                />
              </div>
            </BaseCard>
          </div>

          <div class="space-y-6">
            <BaseCard class="self-start">
              <template #header>
                <h2 class="text-lg font-semibold text-ink-strong">Quick assessment</h2>
              </template>
              <div class="space-y-4">
                <MetricCard
                  v-for="item in assessments"
                  :key="item.label"
                  :label="item.label"
                  :value="item.summary"
                  :description="item.detail"
                  :tone="item.tone"
                />
              </div>
            </BaseCard>

            <BaseCard class="self-start">
              <template #header>
                <h2 class="text-lg font-semibold text-ink-strong">Score formula</h2>
              </template>
              <div class="space-y-3 text-sm text-ink-muted">
                <p>The current frontend fallback score is computed from the release metrics and can later be replaced by the API-provided score.</p>
                <code class="block whitespace-pre-wrap rounded-control border border-hairline p-3 text-xs text-ink-muted">score = round(100 × (0.35×effective + 0.20×dedupe + 0.15×compression + 0.20×newData + 0.10×savedBytes))</code>
              </div>
            </BaseCard>

          </div>
        </div>

        <div v-else class="rounded-card border border-warning/25 bg-warning-soft px-5 py-4 text-sm text-warning">
          Metrics are not available for this release yet.
        </div>
      </section>

      <section v-else-if="activeTab === 'properties'">
        <BaseCard>
          <template #header>
            <h2 class="flex items-center gap-2 font-semibold text-ink-strong">
              <IconListDetails class="w-4 h-4 text-ink-muted" /> Custom Properties
            </h2>
          </template>
          <div v-if="propsLoading" class="text-sm text-ink-subtle">Loading…</div>
          <div v-else-if="parsedProps && Object.keys(parsedProps).length > 0" class="divide-y divide-hairline">
            <div v-for="(val, key) in parsedProps" :key="key" class="flex items-start gap-4 py-2.5">
              <span class="min-w-35 font-mono text-xs font-medium text-ink-muted">{{ key }}</span>
              <span class="break-all text-sm text-ink-strong">{{ val }}</span>
            </div>
          </div>
          <div v-else class="text-sm text-ink-subtle">No custom properties.</div>
        </BaseCard>
      </section>

      <BaseModal v-model:open="downloadOpen" title="Download release" size="lg">
        <div class="space-y-4">
          <p class="text-sm text-ink-muted">Download this release as a package, or reconstruct it from the chunk store with the BinStash client.</p>
          <BaseButton :icon="IconPackage" :href="downloadUrl" target="_blank" @click="downloadOpen = false">Download full release package</BaseButton>
          <div class="border-t border-hairline pt-4">
            <p class="mb-2 text-xs font-semibold uppercase tracking-wide text-ink-subtle">CLI download command</p>
            <div class="flex items-center gap-2">
              <code class="flex-1 break-all rounded-control border border-hairline px-4 py-2.5 font-mono text-xs text-ink-muted">binstash download --tenant {{ tenantId }} --repo {{ repoId }} --version {{ release.version }}</code>
              <BaseButton variant="secondary" size="sm" :icon="cliCopied ? IconCheck : IconCopy" @click="copyCli">{{ cliCopied ? 'Copied!' : 'Copy' }}</BaseButton>
            </div>
          </div>
        </div>
      </BaseModal>
    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted } from 'vue'
import { useRoute } from 'vue-router'
import Tabs from '@/shared/components/navigation/Tabs.vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import {
  Breadcrumbs,
  BaseCard,
  BaseButton,
  BaseModal,
  BaseBadge,
  MetricCard,
} from '@/shared/components/ui'
import {
  IconChevronRight,
  IconTag,
  IconCalendar,
  IconCheck,
  IconCopy,
  IconDownload,
  IconFileText,
  IconListDetails,
  IconPackage,
  IconLayoutDashboard,
  IconChartBar,
} from '@tabler/icons-vue'
import { getRelease, getReleaseProperties, type ReleaseMetricsDto, type ReleaseSummaryDto } from '../../../api/repositories'
import { calculateReleaseScore, getReleaseScoreBadgeClasses, getReleaseScoreLabel } from '@/utils/releaseScore'

type MetricTone = 'neutral' | 'good' | 'bad'

type DisplayMetric = {
  key: string
  label: string
  value: string
  help: string
  description: string
  tone: MetricTone
}

const route = useRoute()
const tenantId = computed(() => route.params.tenantId as string)
const repoId = computed(() => route.params.repoId as string)
const releaseId = computed(() => route.params.releaseId as string)

const loading = ref(true)
const error = ref<string | null>(null)
const release = ref<ReleaseSummaryDto | null>(null)
const repoName = computed(() => release.value?.repository?.name ?? 'Repository')
const metrics = computed<ReleaseMetricsDto | null>(() => release.value?.metrics ?? null)
const activeTab = ref('overview')
const downloadOpen = ref(false)
const detailTabs = [
  { id: 'overview', label: 'Overview', icon: IconLayoutDashboard },
  { id: 'metrics', label: 'Metrics', icon: IconChartBar },
  { id: 'properties', label: 'Properties', icon: IconListDetails },
]

const propsLoading = ref(false)
const properties = ref<string | null>(null)
const parsedProps = computed<Record<string, unknown> | null>(() => {
  if (!properties.value) return null
  try { return JSON.parse(properties.value) } catch { return null }
})

const cliCopied = ref(false)
const releaseScore = computed(() => calculateReleaseScore(metrics.value))
const releaseScoreDisplay = computed(() => releaseScore.value == null ? '—' : `${releaseScore.value}/100`)
const releaseScoreLabel = computed(() => getReleaseScoreLabel(releaseScore.value))

const downloadUrl = computed(() =>
  `/api/tenants/${tenantId.value}/repositories/${repoId.value}/releases/${releaseId.value}/download`,
)

function fmtDateFull(s: string) {
  return new Date(s).toLocaleString(undefined, {
    year: 'numeric', month: 'long', day: 'numeric',
    hour: '2-digit', minute: '2-digit',
  })
}

function formatBytes(bytes: number | null | undefined) {
  if (bytes == null) return '—'
  const units = ['B', 'KB', 'MB', 'GB', 'TB', 'PB']
  let value = Math.abs(bytes)
  let unitIndex = 0
  while (value >= 1024 && unitIndex < units.length - 1) {
    value /= 1024
    unitIndex += 1
  }
  const sign = bytes < 0 ? '-' : ''
  return `${sign}${value >= 100 || value % 1 === 0 ? value.toFixed(0) : value.toFixed(1)} ${units[unitIndex]}`
}

function formatCount(value: number | null | undefined) {
  if (value == null) return '—'
  return new Intl.NumberFormat().format(value)
}

function formatPercent(value: number | null | undefined) {
  if (value == null) return '—'
  const normalized = value <= 1 ? value * 100 : value
  return `${normalized.toFixed(normalized >= 10 ? 0 : 1)}%`
}

function formatRatio(value: number | null | undefined) {
  if (value == null) return '—'
  return `${value.toFixed(value >= 10 ? 1 : 2)}×`
}

function assessNewDataPercent(value: number | null | undefined): MetricTone {
  if (value == null) return 'neutral'
  const normalized = value <= 1 ? value * 100 : value
  if (normalized <= 25) return 'good'
  if (normalized >= 60) return 'bad'
  return 'neutral'
}

function assessPositiveRatio(value: number | null | undefined): MetricTone {
  if (value == null) return 'neutral'
  if (value >= 2) return 'good'
  if (value < 1.1) return 'bad'
  return 'neutral'
}

function assessSavings(value: number | null | undefined): MetricTone {
  if (value == null) return 'neutral'
  if (value > 0) return 'good'
  return 'bad'
}

function assessNewBytes(value: number | null | undefined, total: number | null | undefined): MetricTone {
  if (value == null || total == null || total <= 0) return 'neutral'
  const ratio = value / total
  if (ratio <= 0.25) return 'good'
  if (ratio >= 0.65) return 'bad'
  return 'neutral'
}

const topMetrics = computed<DisplayMetric[]>(() => {
  if (!metrics.value) return []
  return [
    {
      key: 'effective-ratio',
      label: 'Effective ratio',
      value: formatRatio(metrics.value.incrementalEffectiveRatio),
      help: 'Combined storage efficiency from both deduplication and compression for the new data introduced by this release.',
      description: 'Higher is generally better because more logical content is stored per byte of new compressed data.',
      tone: assessPositiveRatio(metrics.value.incrementalEffectiveRatio),
    },
    {
      key: 'new-data-percent',
      label: 'New data share',
      value: formatPercent(metrics.value.newDataPercent),
      help: 'The portion of this release that had to be stored as newly unique data instead of being reused from prior releases.',
      description: 'Lower is generally better because it means the release reused more existing content.',
      tone: assessNewDataPercent(metrics.value.newDataPercent),
    },
    {
      key: 'dedupe-saved',
      label: 'Dedupe savings',
      value: formatBytes(metrics.value.deduplicationSavedBytes),
      help: 'Bytes avoided because chunks already existed and could be referenced instead of stored again.',
      description: 'A strong signal that the release benefited from chunk reuse across versions.',
      tone: assessSavings(metrics.value.deduplicationSavedBytes),
    },
    {
      key: 'compression-saved',
      label: 'Compression savings',
      value: formatBytes(metrics.value.compressionSavedBytes),
      help: 'Bytes saved by compressing the newly stored unique data for this release.',
      description: 'Higher savings indicate compression meaningfully reduced the storage footprint of new content.',
      tone: assessSavings(metrics.value.compressionSavedBytes),
    },
  ]
})

const storageMetrics = computed<DisplayMetric[]>(() => {
  if (!metrics.value) return []
  return [
    {
      key: 'logical-bytes',
      label: 'Logical release size',
      value: formatBytes(metrics.value.totalLogicalBytes),
      help: 'The total uncompressed logical size of all content represented by this release.',
      description: 'This is the user-visible size of the release before considering deduplication or compression.',
      tone: 'neutral',
    },
    {
      key: 'new-unique-bytes',
      label: 'New unique logical bytes',
      value: formatBytes(metrics.value.newUniqueLogicalBytes),
      help: 'The amount of logical content in the release that was not already available from previous chunks and had to be added as new data.',
      description: 'A lower value relative to the logical release size means more reuse from earlier releases.',
      tone: assessNewBytes(metrics.value.newUniqueLogicalBytes, metrics.value.totalLogicalBytes),
    },
    {
      key: 'new-compressed-bytes',
      label: 'New compressed bytes',
      value: formatBytes(metrics.value.newCompressedBytes),
      help: 'The actual compressed storage added by the new unique data introduced by this release.',
      description: 'This reflects the net storage growth attributable to the newly added content.',
      tone: assessNewBytes(metrics.value.newCompressedBytes, metrics.value.totalLogicalBytes),
    },
    {
      key: 'metadata-bytes',
      label: 'Metadata footprint',
      value: formatBytes(metrics.value.metaBytesFull),
      help: 'The bytes required for full release metadata, separate from chunk payload data.',
      description: 'Useful for understanding the overhead of manifest and bookkeeping data.',
      tone: 'neutral',
    },
  ]
})

const efficiencyMetrics = computed<DisplayMetric[]>(() => {
  if (!metrics.value) return []
  return [
    {
      key: 'compression-ratio',
      label: 'Compression ratio',
      value: formatRatio(metrics.value.incrementalCompressionRatio),
      help: 'How much the new unique data shrank once compressed.',
      description: 'Higher values indicate better compression efficiency on the newly stored data.',
      tone: assessPositiveRatio(metrics.value.incrementalCompressionRatio),
    },
    {
      key: 'dedupe-ratio',
      label: 'Deduplication ratio',
      value: formatRatio(metrics.value.incrementalDeduplicationRatio),
      help: 'How effectively this release reused existing chunks instead of introducing brand-new logical content.',
      description: 'Higher values mean more of the release could be reconstructed from already stored data.',
      tone: assessPositiveRatio(metrics.value.incrementalDeduplicationRatio),
    },
    {
      key: 'effective-ratio-detail',
      label: 'Effective ratio',
      value: formatRatio(metrics.value.incrementalEffectiveRatio),
      help: 'A combined end-to-end ratio showing the total storage efficiency achieved after both deduplication and compression.',
      description: 'A high effective ratio usually means the release is very storage-efficient overall.',
      tone: assessPositiveRatio(metrics.value.incrementalEffectiveRatio),
    },
    {
      key: 'compression-savings-detail',
      label: 'Compression saved',
      value: formatBytes(metrics.value.compressionSavedBytes),
      help: 'The raw number of bytes avoided thanks to compression.',
      description: 'This isolates the impact of compression from deduplication.',
      tone: assessSavings(metrics.value.compressionSavedBytes),
    },
    {
      key: 'dedupe-savings-detail',
      label: 'Deduplication saved',
      value: formatBytes(metrics.value.deduplicationSavedBytes),
      help: 'The raw number of bytes avoided because data already existed in prior chunks.',
      description: 'This isolates the storage benefit obtained through chunk reuse.',
      tone: assessSavings(metrics.value.deduplicationSavedBytes),
    },
  ]
})

const compositionMetrics = computed<DisplayMetric[]>(() => {
  if (!metrics.value) return []
  return [
    {
      key: 'files',
      label: 'Files in release',
      value: formatCount(metrics.value.filesInRelease),
      help: 'The total number of files represented by this release snapshot.',
      description: 'Useful for estimating package breadth.',
      tone: 'neutral',
    },
    {
      key: 'components',
      label: 'Components in release',
      value: formatCount(metrics.value.componentsInRelease),
      help: 'The number of higher-level components or package parts included in the release.',
      description: 'Helpful for understanding how the release is structured.',
      tone: 'neutral',
    },
    {
      key: 'chunks',
      label: 'Chunks in release',
      value: formatCount(metrics.value.chunksInRelease),
      help: 'The total chunk references needed to represent this release.',
      description: 'Shows how the release maps onto chunked storage.',
      tone: 'neutral',
    },
    {
      key: 'new-chunks',
      label: 'New chunks',
      value: formatCount(metrics.value.newChunks),
      help: 'The number of chunks that were newly introduced by this release instead of reused from prior releases.',
      description: 'Lower values generally indicate better chunk reuse.',
      tone: 'neutral',
    },
  ]
})

const assessments = computed(() => {
  if (!metrics.value) return []
  const newDataTone = assessNewDataPercent(metrics.value.newDataPercent)
  const effectiveTone = assessPositiveRatio(metrics.value.incrementalEffectiveRatio)
  const newDataLabel = newDataTone === 'good'
    ? 'High reuse'
    : newDataTone === 'bad'
      ? 'Large amount of fresh data'
      : 'Mixed reuse'
  const effectiveLabel = effectiveTone === 'good'
    ? 'Strong efficiency'
    : effectiveTone === 'bad'
      ? 'Limited savings'
      : 'Moderate efficiency'

  return [
    {
      label: 'Reuse assessment',
      summary: newDataLabel,
      badge: newDataTone === 'good' ? 'Good' : newDataTone === 'bad' ? 'Watch' : 'Neutral',
      detail: `New data accounts for ${formatPercent(metrics.value.newDataPercent)} of this release. Lower percentages usually mean stronger deduplication across versions.`,
      tone: newDataTone,
    },
    {
      label: 'Efficiency assessment',
      summary: effectiveLabel,
      badge: effectiveTone === 'good' ? 'Good' : effectiveTone === 'bad' ? 'Low' : 'Moderate',
      detail: `The release achieved an end-to-end effective ratio of ${formatRatio(metrics.value.incrementalEffectiveRatio)}, combining deduplication and compression gains.`,
      tone: effectiveTone,
    },
  ]
})

function scoreBadgeClasses(score?: number | null) {
  return getReleaseScoreBadgeClasses(score)
}

async function copyCli() {
  const cmd = `binstash download --tenant ${tenantId.value} --repo ${repoId.value} --version ${release.value?.version ?? ''}`
  await navigator.clipboard.writeText(cmd)
  cliCopied.value = true
  setTimeout(() => { cliCopied.value = false }, 2000)
}

onMounted(async () => {
  loading.value = true
  error.value = null
  try {
    release.value = await getRelease(tenantId.value, repoId.value, releaseId.value)
    propsLoading.value = true
    try {
      properties.value = await getReleaseProperties(tenantId.value, repoId.value, releaseId.value)
    } catch {
      properties.value = null
    } finally {
      propsLoading.value = false
    }
  } catch (e: any) {
    error.value = e?.message ?? 'Failed to load release.'
  } finally {
    loading.value = false
  }
})
</script>
