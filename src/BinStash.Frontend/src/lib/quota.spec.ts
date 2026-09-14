import { describe, expect, it } from 'vitest'

import { evaluateQuota, QUOTA_WARN_FRACTION, type QuotaUsage } from '@/lib/quota'

const unmetered: QuotaUsage = {
  logicalBytes: 5_000,
  maxStorageBytes: null,
  isLimited: false,
  isStorageAllowed: true,
  isIngestAllowed: true,
  isEgressAllowed: true,
}

const metered = (used: number, limit = 1_000): QuotaUsage => ({
  ...unmetered,
  logicalBytes: used,
  maxStorageBytes: limit,
  isLimited: true,
})

describe('evaluateQuota', () => {
  it('says nothing when there is no plan limit, however much is stored', () => {
    const status = evaluateQuota({ ...unmetered, logicalBytes: Number.MAX_SAFE_INTEGER })

    expect(status.level).toBe('ok')
    expect(status.notable).toBe(false)
    expect(status.fraction).toBeNull()
    expect(status.remainingBytes).toBeNull()
  })

  it('handles absent usage rather than throwing while the query is in flight', () => {
    expect(evaluateQuota(null).level).toBe('ok')
    expect(evaluateQuota(undefined).notable).toBe(false)
  })

  it('stays quiet well below the ceiling', () => {
    const status = evaluateQuota(metered(500))

    expect(status.level).toBe('ok')
    expect(status.notable).toBe(false)
    expect(status.remainingBytes).toBe(500)
  })

  it('warns once the warn threshold is crossed', () => {
    expect(evaluateQuota(metered(799)).level).toBe('ok')
    expect(evaluateQuota(metered(QUOTA_WARN_FRACTION * 1_000)).level).toBe('approaching')
  })

  it('escalates near the ceiling, where the next release may not fit', () => {
    expect(evaluateQuota(metered(950)).level).toBe('critical')
  })

  it('reports the quota as reached exactly at the ceiling, not one byte past it', () => {
    // Enforcement admits a release that lands exactly on the limit, so the workspace is full at
    // that point — reporting "reached" only at limit+1 would contradict the next refusal.
    expect(evaluateQuota(metered(1_000)).level).toBe('exceeded')
    expect(evaluateQuota(metered(1_200)).level).toBe('exceeded')
  })

  it('never reports negative headroom when already over', () => {
    expect(evaluateQuota(metered(1_200)).remainingBytes).toBe(0)
  })

  it('names the operations a switched-off entitlement refuses', () => {
    const status = evaluateQuota({ ...metered(10), isIngestAllowed: false, isEgressAllowed: false })

    expect(status.level).toBe('blocked')
    expect(status.blocked).toEqual(['Uploads', 'Downloads'])
    expect(status.description).toContain('402')
  })

  it('uses a singular title when only one operation is refused', () => {
    expect(evaluateQuota({ ...metered(10), isEgressAllowed: false }).title).toBe('Downloads are blocked')
  })

  it('puts a switched-off entitlement ahead of a comfortable-looking meter', () => {
    // The plan saying no outright does not become less true because storage looks fine.
    const status = evaluateQuota({ ...metered(1), isIngestAllowed: false })

    expect(status.level).toBe('blocked')
  })

  it('treats a zero or absent ceiling as unmetered rather than as a full workspace', () => {
    // The server reads zero as "unset"; rendering it as a zero-byte quota would tell every
    // workspace on such a plan that it is permanently full.
    expect(evaluateQuota({ ...metered(100), maxStorageBytes: 0 }).level).toBe('ok')
    expect(evaluateQuota({ ...metered(100), maxStorageBytes: null }).fraction).toBeNull()
  })
})
