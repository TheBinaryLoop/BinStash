/**
 * Shapes the traffic series the server returns into something a chart can draw.
 *
 * The server sends only buckets that have data, which is the right wire format — an idle
 * weekend should not cost 48 rows of zeroes — but a column chart needs a continuous axis, or
 * Saturday's silence renders as Friday sitting next to Monday.
 */

import { formatBytes } from '@/lib/format'

export type TrafficGrain = 'HOURLY' | 'DAILY'

export interface TrafficPointInput {
  bucketStartUtc: string
  ingressBytes: number
  egressBytes: number
  requestCount: number
}

export interface TrafficBucket {
  start: Date
  ingressBytes: number
  egressBytes: number
  requestCount: number
  totalBytes: number
}

const HOUR_MS = 60 * 60 * 1000
const DAY_MS = 24 * HOUR_MS

/** Matches the server's own ceiling, so a malformed window cannot make the browser draw forever. */
const MAX_BUCKETS = 2200

export function bucketSizeMs(grain: TrafficGrain): number {
  return grain === 'DAILY' ? DAY_MS : HOUR_MS
}

/**
 * Produces one bucket per interval between `from` and `to`, zero-filling the gaps.
 *
 * Buckets are keyed on their exact epoch millisecond rather than on a formatted string: the
 * server aligns them to UTC boundaries, and matching on anything local would shift the whole
 * series for any viewer not on UTC.
 */
export function expandBuckets(fromUtc: string | Date, toUtc: string | Date, grain: TrafficGrain, points: readonly TrafficPointInput[]): TrafficBucket[] {
  const step = bucketSizeMs(grain)
  const from = new Date(fromUtc).getTime()
  const to = new Date(toUtc).getTime()

  if (!Number.isFinite(from) || !Number.isFinite(to) || to <= from) return []

  const byStart = new Map<number, TrafficPointInput>()
  for (const point of points) {
    const at = new Date(point.bucketStartUtc).getTime()
    if (Number.isFinite(at)) byStart.set(at, point)
  }

  const count = Math.min(Math.ceil((to - from) / step), MAX_BUCKETS)
  const buckets: TrafficBucket[] = []

  for (let i = 0; i < count; i++) {
    const start = from + i * step
    const point = byStart.get(start)
    const ingressBytes = point?.ingressBytes ?? 0
    const egressBytes = point?.egressBytes ?? 0

    buckets.push({
      start: new Date(start),
      ingressBytes,
      egressBytes,
      requestCount: point?.requestCount ?? 0,
      totalBytes: ingressBytes + egressBytes,
    })
  }

  return buckets
}

/**
 * Axis ticks on clean byte boundaries.
 */
export function byteTicks(maxValue: number, desiredSteps = 3): number[] {
  if (!(maxValue > 0)) return [0]

  // Rounded up to a power of two within its unit, not to a power of ten. Bytes are binary, so
  // 512 KiB is a round number and 500 KiB is not — and the decimal step would land the axis on
  // labels like "1.37 MiB" that take a moment to compare.
  const rawStep = maxValue / desiredSteps
  const unitExponent = Math.max(0, Math.floor(Math.log(rawStep) / Math.log(1024)))
  const unit = 1024 ** unitExponent
  const step = Math.max(1, 2 ** Math.ceil(Math.log2(rawStep / unit)) * unit)

  // Built by multiplication rather than by accumulating a float, which drifts and puts the top
  // tick a few bytes off a round number.
  const ticks: number[] = []
  for (let i = 0; i * step <= maxValue; i++) ticks.push(i * step)

  // Guarantee the top of the axis is at or above the tallest column, so no bar overshoots it.
  if (ticks[ticks.length - 1] < maxValue) ticks.push(ticks.length * step)

  return ticks
}

/** The scale's top: the highest tick, so columns never exceed the plotted area. */
export function axisMax(buckets: readonly TrafficBucket[]): number {
  const peak = buckets.reduce((max, b) => Math.max(max, b.totalBytes), 0)
  const ticks = byteTicks(peak)
  return Math.max(ticks[ticks.length - 1], 1)
}

export function formatBucketLabel(start: Date, grain: TrafficGrain): string {
  return grain === 'DAILY'
    ? start.toLocaleDateString(undefined, { month: 'short', day: 'numeric' })
    : start.toLocaleString(undefined, { month: 'short', day: 'numeric', hour: 'numeric' })
}

/** The few x labels a narrow chart can fit without collisions. */
export function labelledIndices(count: number, maxLabels = 5): number[] {
  if (count <= 0) return []
  if (count <= maxLabels) return Array.from({ length: count }, (_, i) => i)

  const stride = (count - 1) / (maxLabels - 1)
  const indices = new Set<number>()
  for (let i = 0; i < maxLabels; i++) indices.add(Math.round(i * stride))

  return [...indices].sort((a, b) => a - b)
}

export function formatTrafficBytes(value: number): string {
  return formatBytes(value)
}
