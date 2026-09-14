<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

import {
  axisMax,
  byteTicks,
  expandBuckets,
  formatBucketLabel,
  labelledIndices,
  type TrafficGrain,
  type TrafficPointInput,
} from '@/lib/traffic'
import { formatBytes, formatNumber } from '@/lib/format'

/**
 * Ingress and egress over time, as stacked columns.
 *
 * Stacked rather than mirrored: the operational question is "when did this instance move the
 * most data", and the split between directions is the follow-up. A mirrored pair answers the
 * follow-up first and makes the total something the reader has to add up by eye.
 *
 * The two series colors are fixed hex rather than theme tokens. They are a categorical pair
 * chosen and validated for colorblind separation against both chart surfaces (ΔE ≥ 24 under
 * protanopia and tritanopia); the app's semantic tokens are a single accent hue plus status
 * colors, and status colors must not be reused to mean "series 2".
 */
const props = defineProps<{
  points: readonly TrafficPointInput[]
  fromUtc: string
  toUtc: string
  grain: TrafficGrain
  /** Rendered height of the plot area in px. */
  height?: number
}>()

const PLOT_HEIGHT = computed(() => props.height ?? 140)

const AXIS_GUTTER = 52
const LABEL_BAND = 18
const SURFACE_GAP = 2
const MAX_COLUMN = 24

// The highest gridline sits at y = 0, and its label is centred on it — without a band of space
// above the plot the top label is clipped in half by the viewBox.
const TOP_PAD = 8

/**
 * The viewBox width tracks the element's real width so one unit is one pixel.
 *
 * A fixed viewBox width with a fixed pixel height does not stretch — preserveAspectRatio
 * letterboxes it, so on a card wider than the viewBox the chart sits centred in a band of empty
 * space. Measuring also makes the mark specs mean what they say: a 24px bar cap and a 2px gap are
 * only 24 and 2 real pixels if the scale is 1:1.
 */
const root = ref<HTMLElement | null>(null)
const width = ref(720)
let observer: ResizeObserver | null = null

onMounted(() => {
  if (!root.value || typeof ResizeObserver === 'undefined') return

  observer = new ResizeObserver((entries) => {
    const measured = entries[0]?.contentRect.width
    if (measured && measured > 0) width.value = Math.round(measured)
  })
  observer.observe(root.value)
})

onBeforeUnmount(() => observer?.disconnect())

const buckets = computed(() => expandBuckets(props.fromUtc, props.toUtc, props.grain, props.points))
const scaleMax = computed(() => axisMax(buckets.value))
const ticks = computed(() => byteTicks(buckets.value.reduce((m, b) => Math.max(m, b.totalBytes), 0)))

const plotWidth = computed(() => Math.max(1, width.value - AXIS_GUTTER))
const band = computed(() => (buckets.value.length > 0 ? plotWidth.value / buckets.value.length : plotWidth.value))

/**
 * The gap is capped at a third of the band. A week of hourly buckets gives each column about
 * four pixels, and taking a flat 2px out of that leaves a hairline that reads as an artifact
 * rather than as data.
 */
const surfaceGap = computed(() => Math.min(SURFACE_GAP, band.value / 3))
const columnWidth = computed(() => Math.min(MAX_COLUMN, Math.max(1, band.value - surfaceGap.value)))

const hasTraffic = computed(() => buckets.value.some((b) => b.totalBytes > 0))
const labelled = computed(() => labelledIndices(buckets.value.length))

/**
 * The outermost labels anchor inward. Centred on their band they would sit half outside the
 * viewBox and be clipped — the axis runs to the very edge of the card by design.
 */
function labelAnchor(index: number): 'start' | 'middle' | 'end' {
  if (index === 0) return 'start'
  return index === buckets.value.length - 1 ? 'end' : 'middle'
}

function labelX(index: number): number {
  if (index === 0) return AXIS_GUTTER
  if (index === buckets.value.length - 1) return width.value
  return AXIS_GUTTER + index * band.value + band.value / 2
}

const hovered = ref<number | null>(null)
const hoveredBucket = computed(() => (hovered.value === null ? null : (buckets.value[hovered.value] ?? null)))

function y(value: number): number {
  return TOP_PAD + PLOT_HEIGHT.value - (value / scaleMax.value) * PLOT_HEIGHT.value
}

function columnX(index: number): number {
  return AXIS_GUTTER + index * band.value + (band.value - columnWidth.value) / 2
}

/**
 * Segment heights, with the surface gap taken out of the lower segment so the two never touch.
 * A segment that would round to nothing is dropped rather than drawn as a sliver.
 */
function segments(index: number) {
  const bucket = buckets.value[index]
  const ingressHeight = (bucket.ingressBytes / scaleMax.value) * PLOT_HEIGHT.value
  const egressHeight = (bucket.egressBytes / scaleMax.value) * PLOT_HEIGHT.value

  const result: { key: string; y: number; height: number; className: string; rounded: boolean }[] = []
  const top = TOP_PAD + PLOT_HEIGHT.value - ingressHeight - egressHeight

  if (egressHeight >= 0.5) {
    result.push({ key: 'egress', y: top, height: egressHeight, className: 'fill-[var(--traffic-egress)]', rounded: true })
  }

  if (ingressHeight >= 0.5) {
    const gap = egressHeight >= 0.5 ? surfaceGap.value : 0
    result.push({
      key: 'ingress',
      y: top + egressHeight + gap,
      height: Math.max(0.5, ingressHeight - gap),
      className: 'fill-[var(--traffic-ingress)]',
      rounded: egressHeight < 0.5,
    })
  }

  return result
}

const totals = computed(() => ({
  ingress: buckets.value.reduce((sum, b) => sum + b.ingressBytes, 0),
  egress: buckets.value.reduce((sum, b) => sum + b.egressBytes, 0),
}))
</script>

<template>
  <figure class="traffic-chart space-y-3">
    <!-- Legend: always present for two series, so identity never rests on color alone. -->
    <figcaption class="flex flex-wrap items-center gap-x-4 gap-y-1 text-xs">
      <span class="flex items-center gap-1.5">
        <span class="size-2.5 rounded-[2px] bg-[var(--traffic-ingress)]" aria-hidden="true" />
        <span class="text-muted-foreground">In</span>
        <span class="font-mono tabular-nums">{{ formatBytes(totals.ingress) }}</span>
      </span>
      <span class="flex items-center gap-1.5">
        <span class="size-2.5 rounded-[2px] bg-[var(--traffic-egress)]" aria-hidden="true" />
        <span class="text-muted-foreground">Out</span>
        <span class="font-mono tabular-nums">{{ formatBytes(totals.egress) }}</span>
      </span>
      <span v-if="hoveredBucket" class="text-muted-foreground ml-auto hidden sm:inline">
        {{ formatBucketLabel(hoveredBucket.start, grain) }} ·
        <span class="font-mono tabular-nums">{{ formatBytes(hoveredBucket.ingressBytes) }}</span> in ·
        <span class="font-mono tabular-nums">{{ formatBytes(hoveredBucket.egressBytes) }}</span> out
      </span>
    </figcaption>

    <div ref="root" class="relative">
      <svg
        :viewBox="`0 0 ${width} ${TOP_PAD + PLOT_HEIGHT + LABEL_BAND}`"
        class="w-full"
        :style="{ height: `${TOP_PAD + PLOT_HEIGHT + LABEL_BAND}px` }"
        role="img"
        :aria-label="`Traffic by ${grain === 'DAILY' ? 'day' : 'hour'}: ${formatBytes(totals.ingress)} in, ${formatBytes(totals.egress)} out`"
        @pointerleave="hovered = null"
      >
        <!-- Gridlines: hairline, solid, one step off the surface. -->
        <g>
          <line
            v-for="tick in ticks"
            :key="`grid-${tick}`"
            :x1="AXIS_GUTTER"
            :x2="width"
            :y1="y(tick)"
            :y2="y(tick)"
            class="stroke-border"
            stroke-width="1"
          />
          <text
            v-for="tick in ticks"
            :key="`tick-${tick}`"
            :x="AXIS_GUTTER - 8"
            :y="y(tick) + 3"
            text-anchor="end"
            class="fill-muted-foreground text-[9px] tabular-nums"
          >
            {{ tick === 0 ? '0' : formatBytes(tick, 0) }}
          </text>
        </g>

        <!-- Columns -->
        <g v-for="(bucket, index) in buckets" :key="bucket.start.toISOString()">
          <rect
            v-for="segment in segments(index)"
            :key="segment.key"
            :x="columnX(index)"
            :y="segment.y"
            :width="columnWidth"
            :height="segment.height"
            :rx="segment.rounded ? Math.min(4, columnWidth / 2) : 0"
            :class="segment.className"
            :opacity="hovered === null || hovered === index ? 1 : 0.45"
          />
          <!-- A hit target that spans the full band, so a one-pixel column is still hoverable. -->
          <rect
            :x="AXIS_GUTTER + index * band"
            :y="TOP_PAD"
            :width="band"
            :height="PLOT_HEIGHT"
            fill="transparent"
            @pointerenter="hovered = index"
          >
            <title>
              {{ formatBucketLabel(bucket.start, grain) }} — {{ formatBytes(bucket.ingressBytes) }} in,
              {{ formatBytes(bucket.egressBytes) }} out, {{ formatNumber(bucket.requestCount) }} operations
            </title>
          </rect>
        </g>

        <!-- x labels: thinned so they cannot collide. -->
        <text
          v-for="index in labelled"
          :key="`label-${index}`"
          :x="labelX(index)"
          :y="TOP_PAD + PLOT_HEIGHT + LABEL_BAND - 4"
          :text-anchor="labelAnchor(index)"
          class="fill-muted-foreground text-[9px]"
        >
          {{ buckets[index] ? formatBucketLabel(buckets[index].start, grain) : '' }}
        </text>
      </svg>

      <p
        v-if="!hasTraffic"
        class="text-muted-foreground pointer-events-none absolute inset-0 flex items-center justify-center text-xs"
      >
        No traffic recorded in this period.
      </p>
    </div>

    <!--
      The same numbers as a table, for screen readers and for anyone who cannot use the hover.
      The clip lives on a wrapping div rather than on the table: a table's caption is laid out
      outside the table box, so `sr-only` on the table itself leaves the caption visible and
      overflowing the card.
    -->
    <div class="sr-only">
      <table>
        <caption>
          Traffic by {{ grain === 'DAILY' ? 'day' : 'hour' }}
        </caption>
        <thead>
          <tr><th scope="col">Period</th><th scope="col">In</th><th scope="col">Out</th><th scope="col">Operations</th></tr>
        </thead>
        <tbody>
          <tr v-for="bucket in buckets" :key="`row-${bucket.start.toISOString()}`">
            <th scope="row">{{ formatBucketLabel(bucket.start, grain) }}</th>
            <td>{{ formatBytes(bucket.ingressBytes) }}</td>
            <td>{{ formatBytes(bucket.egressBytes) }}</td>
            <td>{{ formatNumber(bucket.requestCount) }}</td>
          </tr>
        </tbody>
      </table>
    </div>
  </figure>
</template>

<style scoped>
/*
 * Categorical slots 1 and 2 (blue, orange), stepped per mode for the app's card surface and
 * validated for CVD separation against each. Dark is a selected step, not an automatic flip.
 */
.traffic-chart {
  --traffic-ingress: #2a78d6;
  --traffic-egress: #eb6834;
}

:root.dark .traffic-chart {
  --traffic-ingress: #3987e5;
  --traffic-egress: #d95926;
}
</style>
