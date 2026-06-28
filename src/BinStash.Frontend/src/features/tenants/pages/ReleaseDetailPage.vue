<template>
  <div class="space-y-6">
    <nav v-if="!loading && release" class="flex items-center gap-2 text-sm text-gray-500 dark:text-gray-400">
      <router-link :to="`/t/${tenantId}/repositories`" class="hover:text-violet-500 transition">Repositories</router-link>
      <IconChevronRight class="w-4 h-4 text-gray-300 dark:text-gray-600 shrink-0" />
      <router-link :to="`/t/${tenantId}/repositories/${repoId}`" class="hover:text-violet-500 transition truncate max-w-56 sm:max-w-none">
        {{ repoName }}
      </router-link>
      <IconChevronRight class="w-4 h-4 text-gray-300 dark:text-gray-600 shrink-0" />
      <span class="text-gray-700 dark:text-gray-200 font-medium truncate">Release {{ release.version }}</span>
    </nav>

    <div v-if="loading" class="flex justify-center py-24">
      <div class="animate-spin rounded-full h-8 w-8 border-b-2 border-violet-500"></div>
    </div>

    <div v-else-if="error" class="bg-red-50 dark:bg-red-900/20 border border-red-200 dark:border-red-800 rounded-2xl p-4 text-red-700 dark:text-red-400">
      {{ error }}
    </div>

    <template v-else-if="release">
      <section class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-sm">
        <div class="h-1.5 bg-linear-to-r from-violet-500 via-fuchsia-500 to-teal-400" />
        <div class="p-6 lg:p-7">
          <div class="flex flex-col gap-5 xl:flex-row xl:items-start xl:justify-between">
            <div class="min-w-0">
              <div class="flex flex-wrap items-center gap-3 mb-3">
                <span class="inline-flex items-center gap-1.5 rounded-full bg-violet-100 px-3 py-1 text-sm font-semibold text-violet-700 dark:bg-violet-900/30 dark:text-violet-300">
                  <IconTag class="h-3.5 w-3.5" />
                  {{ release.version }}
                </span>
                <span class="inline-flex items-center gap-1.5 rounded-full bg-gray-100 px-3 py-1 text-xs font-medium text-gray-600 dark:bg-gray-700/60 dark:text-gray-300">
                  <IconCalendar class="h-3.5 w-3.5" />
                  Published {{ fmtDateFull(release.createdAt) }}
                </span>
              </div>
              <div class="flex flex-wrap items-center gap-3">
                <h1 class="text-2xl font-bold text-gray-900 dark:text-white">Release details</h1>
                <span class="inline-flex items-center gap-2 rounded-full px-3 py-1 text-sm font-semibold"
                  :class="scoreBadgeClasses(releaseScore)">
                  Score {{ releaseScoreDisplay }}
                  <span class="text-xs font-medium opacity-80">{{ releaseScoreLabel }}</span>
                </span>
              </div>
              <p class="mt-2 max-w-3xl text-sm leading-6 text-gray-500 dark:text-gray-400">
                Lorem ipsum.
              </p>
            </div>

            <div class="flex flex-wrap gap-3 shrink-0">
              <a :href="downloadUrl" target="_blank" class="btn bg-violet-500 hover:bg-violet-600 text-white flex items-center gap-2 self-start">
                <IconDownload class="w-4 h-4" />
                Download
              </a>
            </div>
          </div>
        </div>
      </section>

      <Tabs :tabs="detailTabs" v-model="activeTab" />

      <section v-if="activeTab === 'overview'" class="grid grid-cols-1 gap-6 xl:grid-cols-3">
        <div class="xl:col-span-2 space-y-6">
          <div class="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700/60 shadow-sm overflow-hidden">
            <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700/60 flex items-center justify-between gap-3">
              <h2 class="font-semibold text-gray-800 dark:text-gray-100 flex items-center gap-2">
                <IconFileText class="w-4 h-4 text-gray-400" /> Release Notes
              </h2>
              <span class="inline-flex items-center rounded-full bg-violet-100 px-2.5 py-1 text-xs font-semibold text-violet-700 dark:bg-violet-500/20 dark:text-violet-300">
                Primary summary
              </span>
            </div>
            <div class="p-5">
              <p v-if="release.notes" class="text-sm text-gray-600 dark:text-gray-400 whitespace-pre-wrap leading-relaxed">{{ release.notes }}</p>
              <p v-else class="text-sm text-gray-400">No release notes were provided for this version.</p>
            </div>
          </div>
        </div>

        <div class="space-y-6">
          <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-sm overflow-hidden">
            <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
              <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Release details</h2>
            </div>
            <dl class="divide-y divide-gray-100 dark:divide-gray-700/60">
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Version</dt>
                <dd class="mt-1 text-sm font-semibold text-gray-900 dark:text-white font-mono">{{ release.version }}</dd>
              </div>
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Repository</dt>
                <dd class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ repoName }}</dd>
              </div>
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Published</dt>
                <dd class="mt-1 text-sm font-semibold text-gray-900 dark:text-white">{{ fmtDateFull(release.createdAt) }}</dd>
              </div>
              <div class="px-5 py-4">
                <dt class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Release ID</dt>
                <dd class="mt-1 break-all text-xs font-mono text-gray-500 dark:text-gray-400">{{ releaseId }}</dd>
              </div>
            </dl>
          </div>

          <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-sm overflow-hidden">
            <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
              <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Quick assessment</h2>
            </div>
            <div class="space-y-4 p-5">
              <div
                v-for="item in assessments"
                :key="item.label"
                class="rounded-xl border p-4"
                :class="metricCardClasses(item.tone)"
              >
                <div class="flex items-center justify-between gap-3">
                  <div>
                    <div class="text-xs uppercase tracking-wide" :class="metricEyebrowClasses(item.tone)">{{ item.label }}</div>
                    <div class="mt-1 text-sm font-semibold" :class="metricValueClasses(item.tone)">{{ item.summary }}</div>
                  </div>
                  <span class="inline-flex items-center rounded-full px-2.5 py-1 text-[11px] font-semibold"
                    :class="badgeClasses(item.tone)">
                    {{ item.badge }}
                  </span>
                </div>
                <p class="mt-3 text-sm leading-6" :class="metricBodyClasses(item.tone)">{{ item.detail }}</p>
              </div>
            </div>
          </div>
        </div>
      </section>

      <section v-else-if="activeTab === 'metrics'">
        <div v-if="metrics" class="grid grid-cols-1 gap-6 xl:grid-cols-3">
          <div class="xl:col-span-2 space-y-6">
            <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-sm overflow-visible">
              <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
                <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Key outcomes</h2>
                <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">The most important quality signals for this release, based on reuse and storage efficiency.</p>
              </div>
              <div class="grid grid-cols-1 gap-4 p-5 md:grid-cols-2 xl:grid-cols-4">
                <article
                  v-for="metric in topMetrics"
                  :key="metric.key"
                  class="rounded-2xl border p-4 shadow-xs"
                  :class="metricCardClasses(metric.tone)"
                >
                  <div class="flex items-start justify-between gap-3">
                    <div>
                      <div class="text-xs uppercase tracking-wide" :class="metricEyebrowClasses(metric.tone)">{{ metric.label }}</div>
                      <div class="mt-2 text-2xl font-bold" :class="metricValueClasses(metric.tone)">{{ metric.value }}</div>
                    </div>
                    <MetricInfo :text="metric.help" />
                  </div>
                  <p class="mt-3 text-sm leading-5" :class="metricBodyClasses(metric.tone)">{{ metric.description }}</p>
                </article>
              </div>
            </div>

            <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-sm overflow-visible">
              <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
                <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Storage impact</h2>
                <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">How much logical content this release represents versus how much truly new storage it introduced.</p>
              </div>
              <div class="grid grid-cols-1 gap-4 p-5 md:grid-cols-2">
                <article
                  v-for="metric in storageMetrics"
                  :key="metric.key"
                  class="rounded-xl border p-4"
                  :class="metricCardClasses(metric.tone)"
                >
                  <div class="flex items-start justify-between gap-3">
                    <div>
                      <div class="flex items-center gap-2">
                        <h3 class="text-sm font-semibold" :class="metricValueClasses(metric.tone)">{{ metric.label }}</h3>
                      </div>
                      <p class="mt-2 text-xl font-bold" :class="metricValueClasses(metric.tone)">{{ metric.value }}</p>
                    </div>
                    <MetricInfo :text="metric.help" />
                  </div>
                  <p class="mt-3 text-sm leading-6" :class="metricBodyClasses(metric.tone)">{{ metric.description }}</p>
                </article>
              </div>
            </div>

            <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-sm overflow-visible">
              <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
                <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Deduplication & compression</h2>
                <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">These metrics show the quality of reuse and compression achieved for this release.</p>
              </div>
              <div class="grid grid-cols-1 gap-4 p-5 md:grid-cols-2 xl:grid-cols-3">
                <article
                  v-for="metric in efficiencyMetrics"
                  :key="metric.key"
                  class="rounded-xl border p-4"
                  :class="metricCardClasses(metric.tone)"
                >
                  <div class="flex items-start justify-between gap-3">
                    <div>
                      <h3 class="text-sm font-semibold" :class="metricValueClasses(metric.tone)">{{ metric.label }}</h3>
                      <p class="mt-2 text-xl font-bold" :class="metricValueClasses(metric.tone)">{{ metric.value }}</p>
                    </div>
                    <MetricInfo :text="metric.help" />
                  </div>
                  <p class="mt-3 text-sm leading-6" :class="metricBodyClasses(metric.tone)">{{ metric.description }}</p>
                </article>
              </div>
            </div>

            <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-sm overflow-visible">
              <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
                <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Release composition</h2>
                <p class="mt-1 text-sm text-gray-500 dark:text-gray-400">Counts and metadata that describe what is inside the release and how it maps to chunked storage.</p>
              </div>
              <div class="grid grid-cols-1 gap-4 p-5 md:grid-cols-2 xl:grid-cols-4">
                <article
                  v-for="metric in compositionMetrics"
                  :key="metric.key"
                  class="rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50/80 dark:bg-gray-900/30 p-4"
                >
                  <div class="flex items-start justify-between gap-3">
                    <div>
                      <h3 class="text-sm font-semibold text-gray-900 dark:text-white">{{ metric.label }}</h3>
                      <p class="mt-2 text-xl font-bold text-gray-900 dark:text-white">{{ metric.value }}</p>
                    </div>
                    <MetricInfo :text="metric.help" />
                  </div>
                  <p class="mt-3 text-sm leading-6 text-gray-500 dark:text-gray-400">{{ metric.description }}</p>
                </article>
              </div>
            </div>
          </div>

          <div class="space-y-6">
            <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-sm overflow-hidden self-start">
              <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
                <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Quick assessment</h2>
              </div>
              <div class="space-y-4 p-5">
                <div
                  v-for="item in assessments"
                  :key="item.label"
                  class="rounded-xl border p-4"
                  :class="metricCardClasses(item.tone)"
                >
                  <div class="flex items-center justify-between gap-3">
                    <div>
                      <div class="text-xs uppercase tracking-wide" :class="metricEyebrowClasses(item.tone)">{{ item.label }}</div>
                      <div class="mt-1 text-sm font-semibold" :class="metricValueClasses(item.tone)">{{ item.summary }}</div>
                    </div>
                    <span class="inline-flex items-center rounded-full px-2.5 py-1 text-[11px] font-semibold"
                      :class="badgeClasses(item.tone)">
                      {{ item.badge }}
                    </span>
                  </div>
                  <p class="mt-3 text-sm leading-6" :class="metricBodyClasses(item.tone)">{{ item.detail }}</p>
                </div>
              </div>
            </div>

            <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-sm overflow-hidden self-start">
              <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
                <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Score formula</h2>
              </div>
              <div class="p-5 space-y-3 text-sm text-gray-600 dark:text-gray-300">
                <p>The current frontend fallback score is computed from the release metrics and can later be replaced by the API-provided score.</p>
                <code class="block whitespace-pre-wrap rounded-xl border border-gray-200 dark:border-gray-700/60 bg-gray-50 dark:bg-gray-900/30 p-3 text-xs text-gray-700 dark:text-gray-200">score = round(100 × (0.35×effective + 0.20×dedupe + 0.15×compression + 0.20×newData + 0.10×savedBytes))</code>
              </div>
            </div>

            <div class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-sm overflow-hidden self-start">
              <div class="border-b border-gray-200 dark:border-gray-700/60 px-5 py-4">
                <h2 class="text-lg font-semibold text-gray-900 dark:text-white">Metric coverage</h2>
              </div>
              <dl class="divide-y divide-gray-100 dark:divide-gray-700/60">
                <div class="px-5 py-4">
                  <dt class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Storage metrics</dt>
                  <dd class="mt-1 text-sm font-medium text-gray-900 dark:text-white">4 values</dd>
                </div>
                <div class="px-5 py-4">
                  <dt class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Efficiency metrics</dt>
                  <dd class="mt-1 text-sm font-medium text-gray-900 dark:text-white">5 values</dd>
                </div>
                <div class="px-5 py-4">
                  <dt class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Composition metrics</dt>
                  <dd class="mt-1 text-sm font-medium text-gray-900 dark:text-white">4 values</dd>
                </div>
                <div class="px-5 py-4">
                  <dt class="text-xs uppercase tracking-wide text-gray-400 dark:text-gray-500">Primary signal</dt>
                  <dd class="mt-1 text-sm font-medium text-gray-900 dark:text-white">New data share</dd>
                </div>
              </dl>
            </div>
          </div>
        </div>

        <div v-else class="rounded-2xl border border-amber-200 dark:border-amber-500/20 bg-amber-50 dark:bg-amber-500/10 px-5 py-4 text-sm text-amber-700 dark:text-amber-300">
          Metrics are not available for this release yet.
        </div>
      </section>

      <section v-else-if="activeTab === 'properties'">
        <div class="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700/60 shadow-sm overflow-hidden">
          <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700/60 flex items-center justify-between">
            <h2 class="font-semibold text-gray-800 dark:text-gray-100 flex items-center gap-2">
              <IconListDetails class="w-4 h-4 text-gray-400" /> Custom Properties
            </h2>
          </div>
          <div class="p-5">
            <div v-if="propsLoading" class="text-sm text-gray-400">Loading…</div>
            <div v-else-if="parsedProps && Object.keys(parsedProps).length > 0" class="divide-y divide-gray-100 dark:divide-gray-700/60">
              <div v-for="(val, key) in parsedProps" :key="key" class="py-2.5 flex items-start gap-4">
                <span class="text-xs font-mono font-medium text-gray-600 dark:text-gray-400 min-w-35">{{ key }}</span>
                <span class="text-sm text-gray-800 dark:text-gray-200 break-all">{{ val }}</span>
              </div>
            </div>
            <div v-else class="text-sm text-gray-400">No custom properties.</div>
          </div>
        </div>
      </section>

      <section v-else-if="activeTab === 'download'">
        <div class="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700/60 shadow-sm overflow-hidden">
          <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700/60">
            <h2 class="font-semibold text-gray-800 dark:text-gray-100 flex items-center gap-2">
              <IconDownload class="w-4 h-4 text-gray-400" /> Download
            </h2>
          </div>
          <div class="p-5 space-y-3">
            <p class="text-sm text-gray-500 dark:text-gray-400">Download this release as a package. Use the BinStash client to reconstruct files from the chunk store.</p>
            <div class="flex flex-wrap gap-3">
              <a :href="downloadUrl" target="_blank" class="btn bg-violet-500 hover:bg-violet-600 text-white flex items-center gap-2 text-sm">
                <IconPackage class="w-4 h-4" /> Full Release Package
              </a>
            </div>
            <div class="mt-4 pt-4 border-t border-gray-100 dark:border-gray-700">
              <p class="text-xs font-semibold text-gray-500 dark:text-gray-400 mb-2 uppercase">CLI Download Command</p>
              <div class="flex items-center gap-2">
                <code class="flex-1 text-xs bg-gray-50 dark:bg-gray-700/50 border border-gray-200 dark:border-gray-700 rounded-lg px-4 py-2.5 font-mono text-gray-700 dark:text-gray-300">
                  binstash download --tenant {{ tenantId }} --repo {{ repoId }} --version {{ release.version }}
                </code>
                <button @click="copyCli" class="btn border border-gray-200 dark:border-gray-700 text-gray-600 dark:text-gray-400 text-xs shrink-0">
                  {{ cliCopied ? 'Copied!' : 'Copy' }}
                </button>
              </div>
            </div>
          </div>
        </div>
      </section>

      <div v-if="false" class="grid grid-cols-1 gap-6 xl:grid-cols-2">
        <div v-if="release.notes" class="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700/60 shadow-sm overflow-hidden">
          <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700/60">
            <h2 class="font-semibold text-gray-800 dark:text-gray-100 flex items-center gap-2">
              <IconFileText class="w-4 h-4 text-gray-400" /> Release Notes
            </h2>
          </div>
          <div class="p-5">
            <p class="text-sm text-gray-600 dark:text-gray-400 whitespace-pre-wrap leading-relaxed">{{ release.notes }}</p>
          </div>
        </div>

        <div v-if="properties || propsLoading" class="bg-white dark:bg-gray-800 rounded-2xl border border-gray-200 dark:border-gray-700/60 shadow-sm overflow-hidden">
          <div class="px-5 py-4 border-b border-gray-200 dark:border-gray-700/60 flex items-center justify-between">
            <h2 class="font-semibold text-gray-800 dark:text-gray-100 flex items-center gap-2">
              <IconListDetails class="w-4 h-4 text-gray-400" /> Custom Properties
            </h2>
          </div>
          <div class="p-5">
            <div v-if="propsLoading" class="text-sm text-gray-400">Loading…</div>
            <div v-else-if="parsedProps && Object.keys(parsedProps).length > 0" class="divide-y divide-gray-100 dark:divide-gray-700/60">
              <div v-for="(val, key) in parsedProps" :key="key" class="py-2.5 flex items-start gap-4">
                <span class="text-xs font-mono font-medium text-gray-600 dark:text-gray-400 min-w-35">{{ key }}</span>
                <span class="text-sm text-gray-800 dark:text-gray-200 break-all">{{ val }}</span>
              </div>
            </div>
            <div v-else class="text-sm text-gray-400">No custom properties.</div>
          </div>
        </div>
      </div>

    </template>
  </div>
</template>

<script setup lang="ts">
import { ref, computed, onMounted, defineComponent, h } from 'vue'
import { useRoute } from 'vue-router'
import Tabs from '@/shared/components/navigation/Tabs.vue'
import {
  IconChevronRight,
  IconTag,
  IconCalendar,
  IconDownload,
  IconFileText,
  IconListDetails,
  IconPackage,
  IconLayoutDashboard,
  IconChartBar,
} from '@tabler/icons-vue'
import Tooltip from '../../../components/Tooltip.vue'
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

const MetricInfo = defineComponent({
  name: 'MetricInfo',
  props: {
    text: {
      type: String,
      required: true,
    },
  },
  setup(props) {
    return () => h(Tooltip as any, { size: 'md', bg: 'dark' }, {
      default: () => h('div', { class: 'text-xs leading-5 text-gray-200 max-w-56' }, props.text),
    })
  },
})

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
const detailTabs = [
  { id: 'overview', label: 'Overview', icon: IconLayoutDashboard },
  { id: 'metrics', label: 'Metrics', icon: IconChartBar },
  { id: 'properties', label: 'Properties', icon: IconListDetails },
  { id: 'download', label: 'Download', icon: IconDownload },
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

function metricCardClasses(tone: MetricTone) {
  switch (tone) {
    case 'good':
      return 'border-emerald-200 bg-emerald-50 dark:border-emerald-500/20 dark:bg-emerald-500/10'
    case 'bad':
      return 'border-amber-200 bg-amber-50 dark:border-amber-500/20 dark:bg-amber-500/10'
    default:
      return 'border-gray-200 bg-gray-50/80 dark:border-gray-700/60 dark:bg-gray-900/30'
  }
}

function metricEyebrowClasses(tone: MetricTone) {
  switch (tone) {
    case 'good':
      return 'text-emerald-600 dark:text-emerald-300'
    case 'bad':
      return 'text-amber-600 dark:text-amber-300'
    default:
      return 'text-gray-400 dark:text-gray-500'
  }
}

function metricValueClasses(tone: MetricTone) {
  switch (tone) {
    case 'good':
      return 'text-emerald-900 dark:text-emerald-100'
    case 'bad':
      return 'text-amber-900 dark:text-amber-100'
    default:
      return 'text-gray-900 dark:text-white'
  }
}

function metricBodyClasses(tone: MetricTone) {
  switch (tone) {
    case 'good':
      return 'text-emerald-700 dark:text-emerald-200'
    case 'bad':
      return 'text-amber-700 dark:text-amber-200'
    default:
      return 'text-gray-500 dark:text-gray-400'
  }
}

function badgeClasses(tone: MetricTone) {
  switch (tone) {
    case 'good':
      return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300'
    case 'bad':
      return 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-300'
    default:
      return 'bg-gray-100 text-gray-600 dark:bg-gray-700/60 dark:text-gray-300'
  }
}

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