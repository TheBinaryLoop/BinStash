import type { ReleaseMetricsDto } from '@/api/repositories'

function clamp(value: number, min: number, max: number) {
  return Math.min(Math.max(value, min), max)
}

function normalizePercent(value: number | null | undefined) {
  if (value == null) return 0
  return value <= 1 ? clamp(value, 0, 1) : clamp(value / 100, 0, 1)
}

export function calculateReleaseScore(metrics?: ReleaseMetricsDto | null): number | null {
  if (!metrics) return null

  const logicalBytes = Math.max(metrics.totalLogicalBytes ?? 0, 1)
  const effectiveScore = clamp(((metrics.incrementalEffectiveRatio ?? 1) - 1) / 3, 0, 1)
  const dedupeScore = clamp(((metrics.incrementalDeduplicationRatio ?? 1) - 1) / 4, 0, 1)
  const compressionScore = clamp(((metrics.incrementalCompressionRatio ?? 1) - 1) / 2, 0, 1)
  const newDataScore = 1 - normalizePercent(metrics.newDataPercent)
  const savedBytesScore = clamp(
    0.7 * ((metrics.deduplicationSavedBytes ?? 0) / logicalBytes)
      + 0.3 * ((metrics.compressionSavedBytes ?? 0) / logicalBytes),
    0,
    1,
  )

  return Math.round(
    100 * (
      0.35 * effectiveScore
      + 0.20 * dedupeScore
      + 0.15 * compressionScore
      + 0.20 * newDataScore
      + 0.10 * savedBytesScore
    ),
  )
}

export function getReleaseScoreTone(score?: number | null): 'excellent' | 'good' | 'mixed' | 'poor' | 'unknown' {
  if (score == null) return 'unknown'
  if (score >= 85) return 'excellent'
  if (score >= 70) return 'good'
  if (score >= 50) return 'mixed'
  return 'poor'
}

export function getReleaseScoreLabel(score?: number | null) {
  const tone = getReleaseScoreTone(score)

  switch (tone) {
    case 'excellent':
      return 'Excellent'
    case 'good':
      return 'Good'
    case 'mixed':
      return 'Mixed'
    case 'poor':
      return 'Poor'
    default:
      return 'Unrated'
  }
}

export function getReleaseScoreBadgeClasses(score?: number | null) {
  const tone = getReleaseScoreTone(score)

  switch (tone) {
    case 'excellent':
      return 'bg-emerald-100 text-emerald-700 dark:bg-emerald-500/20 dark:text-emerald-300'
    case 'good':
      return 'bg-teal-100 text-teal-700 dark:bg-teal-500/20 dark:text-teal-300'
    case 'mixed':
      return 'bg-amber-100 text-amber-700 dark:bg-amber-500/20 dark:text-amber-300'
    case 'poor':
      return 'bg-rose-100 text-rose-700 dark:bg-rose-500/20 dark:text-rose-300'
    default:
      return 'bg-gray-100 text-gray-600 dark:bg-gray-700/60 dark:text-gray-300'
  }
}