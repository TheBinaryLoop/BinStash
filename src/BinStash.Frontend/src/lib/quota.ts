/**
 * Turns the raw usage figures into the one thing a workspace actually needs from them: whether
 * anything is about to stop working, and what.
 *
 * Kept out of the components because both the dashboard and the usage page have to reach the same
 * verdict — a banner that says "all good" on one screen while the other shows a red meter is worse
 * than no banner at all.
 *
 * The wording here mirrors what the server does rather than describing a plan in the abstract:
 * uploads are refused when an ingest session is opened or finalized, downloads when the release is
 * requested, and each refusal is an HTTP 402. A workspace whose CI has started failing should be
 * able to match what it sees in its build log to what it sees here.
 */

/** Fraction of the storage ceiling at which the workspace is warned. */
export const QUOTA_WARN_FRACTION = 0.8

/** Fraction at which the warning turns urgent — close enough that the next release may not fit. */
export const QUOTA_CRITICAL_FRACTION = 0.95

export interface QuotaUsage {
  logicalBytes: number
  maxStorageBytes?: number | null
  isLimited: boolean
  isStorageAllowed: boolean
  isIngestAllowed: boolean
  isEgressAllowed: boolean
}

export type QuotaLevel = 'ok' | 'approaching' | 'critical' | 'exceeded' | 'blocked'

export interface QuotaStatus {
  level: QuotaLevel
  /** True for anything the workspace should be told about — drives whether a banner renders. */
  notable: boolean
  /** Operations the plan currently refuses. Empty unless <c>level</c> is 'blocked'. */
  blocked: string[]
  title: string
  description: string
  /** Share of the storage ceiling in use, or null when there is no ceiling. */
  fraction: number | null
  /** Bytes still available, or null when there is no ceiling. Never negative. */
  remainingBytes: number | null
}

/**
 * A ceiling is only real if it is a positive, finite number. The open-source billing default
 * reports `Number.MAX_SAFE_INTEGER`-scale values to mean "unmetered", and the server treats zero
 * as unset rather than as a workspace that may store nothing — so neither is a limit to draw.
 */
function ceilingOf(usage: QuotaUsage): number | null {
  if (!usage.isLimited) return null
  const limit = usage.maxStorageBytes
  if (limit == null || !Number.isFinite(limit) || limit <= 0) return null
  return limit
}

export function evaluateQuota(usage: QuotaUsage | null | undefined): QuotaStatus {
  if (!usage) {
    return { level: 'ok', notable: false, blocked: [], title: '', description: '', fraction: null, remainingBytes: null }
  }

  const limit = ceilingOf(usage)
  const fraction = limit === null ? null : usage.logicalBytes / limit
  const remainingBytes = limit === null ? null : Math.max(0, limit - usage.logicalBytes)

  // An explicit entitlement being switched off outranks any measurement. It is the plan saying no
  // outright, and it does not become less true because storage happens to look comfortable.
  const blocked: string[] = []
  if (!usage.isIngestAllowed) blocked.push('Uploads')
  if (!usage.isEgressAllowed) blocked.push('Downloads')
  if (!usage.isStorageAllowed) blocked.push('Storage growth')

  if (blocked.length > 0) {
    return {
      level: 'blocked',
      notable: true,
      blocked,
      title: blocked.length === 1 ? `${blocked[0]} are blocked` : 'Some operations are blocked',
      description: `This workspace has reached a plan limit. ${blocked.join(' and ')} will fail with HTTP 402 until usage drops or the plan changes.`,
      fraction,
      remainingBytes,
    }
  }

  if (limit !== null && usage.logicalBytes >= limit) {
    return {
      level: 'exceeded',
      notable: true,
      blocked: [],
      title: 'Storage quota reached',
      description: 'New uploads are refused until usage drops or the plan changes. Deleting releases frees the quota back up; the space itself is reclaimed by the next garbage collection.',
      fraction,
      remainingBytes,
    }
  }

  if (fraction !== null && fraction >= QUOTA_CRITICAL_FRACTION) {
    return {
      level: 'critical',
      notable: true,
      blocked: [],
      title: 'Almost out of storage',
      description: 'A release that does not fit under the ceiling is refused when it finalizes, after it has already been uploaded. Free up space or raise the plan before the next build.',
      fraction,
      remainingBytes,
    }
  }

  if (fraction !== null && fraction >= QUOTA_WARN_FRACTION) {
    return {
      level: 'approaching',
      notable: true,
      blocked: [],
      title: 'Approaching the storage quota',
      description: 'Still room for now, but worth planning a cleanup or a plan change before uploads start being refused.',
      fraction,
      remainingBytes,
    }
  }

  return { level: 'ok', notable: false, blocked: [], title: '', description: '', fraction, remainingBytes }
}
