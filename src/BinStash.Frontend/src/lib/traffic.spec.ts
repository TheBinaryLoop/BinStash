import { describe, expect, it } from 'vitest'

import { axisMax, byteTicks, expandBuckets, labelledIndices, type TrafficPointInput } from '@/lib/traffic'

const point = (iso: string, ingress: number, egress: number): TrafficPointInput => ({
  bucketStartUtc: iso,
  ingressBytes: ingress,
  egressBytes: egress,
  requestCount: 1,
})

describe('expandBuckets', () => {
  it('fills the gaps the server leaves out', () => {
    // The wire format omits empty buckets; a column chart needs them back, or an idle
    // stretch renders as if it never happened.
    const buckets = expandBuckets('2026-09-14T00:00:00Z', '2026-09-14T04:00:00Z', 'HOURLY', [
      point('2026-09-14T00:00:00Z', 100, 0),
      point('2026-09-14T03:00:00Z', 0, 50),
    ])

    expect(buckets).toHaveLength(4)
    expect(buckets.map((b) => b.totalBytes)).toEqual([100, 0, 0, 50])
  })

  it('matches buckets on the instant, not on a formatted string', () => {
    // Keying on anything local would shift the whole series for a viewer outside UTC.
    const buckets = expandBuckets('2026-09-14T00:00:00Z', '2026-09-14T02:00:00Z', 'HOURLY', [
      point('2026-09-14T01:00:00.000+00:00', 7, 3),
    ])

    expect(buckets[1].ingressBytes).toBe(7)
    expect(buckets[1].egressBytes).toBe(3)
  })

  it('steps by a day at daily grain', () => {
    const buckets = expandBuckets('2026-09-01T00:00:00Z', '2026-09-04T00:00:00Z', 'DAILY', [])

    expect(buckets).toHaveLength(3)
    expect(buckets[1].start.toISOString()).toBe('2026-09-02T00:00:00.000Z')
  })

  it('returns nothing for an inverted or unusable window', () => {
    expect(expandBuckets('2026-09-14T04:00:00Z', '2026-09-14T00:00:00Z', 'HOURLY', [])).toEqual([])
    expect(expandBuckets('not a date', '2026-09-14T00:00:00Z', 'HOURLY', [])).toEqual([])
  })

  it('caps the bucket count so a bad window cannot hang the render', () => {
    const buckets = expandBuckets('1990-01-01T00:00:00Z', '2030-01-01T00:00:00Z', 'HOURLY', [])
    expect(buckets.length).toBeLessThanOrEqual(2200)
  })

  it('carries ingress and egress separately as well as their total', () => {
    const [bucket] = expandBuckets('2026-09-14T00:00:00Z', '2026-09-14T01:00:00Z', 'HOURLY', [point('2026-09-14T00:00:00Z', 10, 4)])

    expect(bucket.ingressBytes).toBe(10)
    expect(bucket.egressBytes).toBe(4)
    expect(bucket.totalBytes).toBe(14)
  })
})

describe('byteTicks', () => {
  it('steps on binary boundaries so labels read as sizes', () => {
    expect(byteTicks(1024 * 1024)).toEqual([0, 512 * 1024, 1024 * 1024])
  })

  it('always reaches at least the peak', () => {
    for (const peak of [1, 999, 1025, 5_000_000, 1024 ** 3 * 7]) {
      const ticks = byteTicks(peak)
      expect(ticks[ticks.length - 1]).toBeGreaterThanOrEqual(peak)
    }
  })

  it('degrades to a single zero tick when there is no traffic', () => {
    expect(byteTicks(0)).toEqual([0])
  })
})

describe('axisMax', () => {
  it('is never zero, so an empty chart still has a scale to divide by', () => {
    expect(axisMax([])).toBeGreaterThan(0)
  })

  it('sits at or above the tallest column', () => {
    const buckets = expandBuckets('2026-09-14T00:00:00Z', '2026-09-14T02:00:00Z', 'HOURLY', [point('2026-09-14T00:00:00Z', 900, 300)])

    expect(axisMax(buckets)).toBeGreaterThanOrEqual(1200)
  })
})

describe('labelledIndices', () => {
  it('labels every bucket when they all fit', () => {
    expect(labelledIndices(4)).toEqual([0, 1, 2, 3])
  })

  it('thins out evenly and always keeps both ends', () => {
    const indices = labelledIndices(168, 5)

    expect(indices[0]).toBe(0)
    expect(indices[indices.length - 1]).toBe(167)
    expect(indices.length).toBeLessThanOrEqual(5)
  })

  it('handles an empty series', () => {
    expect(labelledIndices(0)).toEqual([])
  })
})
