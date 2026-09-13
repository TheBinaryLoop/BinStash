/**
 * Formatting helpers for the numbers this product is made of.
 *
 * Sizes use binary units (KiB/MiB/…) rather than decimal ones on purpose: chunk
 * sizes, boundary masks and store layout are all powers of two, so showing
 * "8.0 MiB" next to a configured 8388608-byte average chunk is the only
 * rendering that lets the two be compared.
 */

const BYTE_UNITS = ['B', 'KiB', 'MiB', 'GiB', 'TiB', 'PiB', 'EiB'] as const

export function formatBytes(bytes: number | null | undefined, fractionDigits = 1): string {
  if (bytes == null || Number.isNaN(bytes)) return '—'
  if (bytes === 0) return '0 B'

  const negative = bytes < 0
  let value = Math.abs(bytes)
  let unit = 0

  while (value >= 1024 && unit < BYTE_UNITS.length - 1) {
    value /= 1024
    unit += 1
  }

  // Whole bytes are never fractional; anything larger reads better with one decimal.
  const digits = unit === 0 ? 0 : fractionDigits
  return `${negative ? '-' : ''}${value.toFixed(digits)} ${BYTE_UNITS[unit]}`
}

export function formatNumber(value: number | null | undefined): string {
  if (value == null || Number.isNaN(value)) return '—'
  return new Intl.NumberFormat(undefined).format(value)
}

export function formatPercent(fraction: number | null | undefined, fractionDigits = 1): string {
  if (fraction == null || Number.isNaN(fraction)) return '—'
  return `${(fraction * 100).toFixed(fractionDigits)}%`
}

/**
 * Renders a dedup/compression ratio as a reduction percentage.
 * The server reports ratios as stored/original, so 0.25 means 75% smaller.
 */
export function formatReduction(ratio: number | null | undefined): string {
  if (ratio == null || Number.isNaN(ratio)) return '—'
  return formatPercent(Math.max(0, 1 - ratio), 0)
}

export function formatDate(value: string | Date | null | undefined): string {
  if (!value) return '—'
  const date = typeof value === 'string' ? new Date(value) : value
  if (Number.isNaN(date.getTime())) return '—'

  return new Intl.DateTimeFormat(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  }).format(date)
}

export function formatDateOnly(value: string | Date | null | undefined): string {
  if (!value) return '—'
  const date = typeof value === 'string' ? new Date(value) : value
  if (Number.isNaN(date.getTime())) return '—'
  return new Intl.DateTimeFormat(undefined, { dateStyle: 'medium' }).format(date)
}

const RELATIVE_STEPS: Array<[Intl.RelativeTimeFormatUnit, number]> = [
  ['second', 60],
  ['minute', 60],
  ['hour', 24],
  ['day', 7],
  ['week', 4.348],
  ['month', 12],
  ['year', Number.POSITIVE_INFINITY],
]

export function formatRelative(value: string | Date | null | undefined): string {
  if (!value) return '—'
  const date = typeof value === 'string' ? new Date(value) : value
  if (Number.isNaN(date.getTime())) return '—'

  const formatter = new Intl.RelativeTimeFormat(undefined, { numeric: 'auto' })
  let delta = (date.getTime() - Date.now()) / 1000

  for (const [unit, step] of RELATIVE_STEPS) {
    if (Math.abs(delta) < step) return formatter.format(Math.round(delta), unit)
    delta /= step
  }

  return formatter.format(Math.round(delta), 'year')
}

/** Shortens a hash or id for display while keeping it recognisable. */
export function shortId(value: string | null | undefined, head = 8): string {
  if (!value) return '—'
  return value.length <= head ? value : value.slice(0, head)
}

export function initialsOf(first?: string | null, last?: string | null, fallback = '?'): string {
  const initials = `${first?.[0] ?? ''}${last?.[0] ?? ''}`.trim()
  return initials ? initials.toUpperCase() : fallback
}
