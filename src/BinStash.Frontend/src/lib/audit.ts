/**
 * Making audit entries readable.
 *
 * An entry arrives as a dotted action code and a free-form JSON blob written by whichever
 * mutation produced it. That is the right shape to *store* — it survives new actions without a
 * schema change — and the wrong shape to read. Everything here exists to close that gap without
 * the UI needing a case for every action the server might grow.
 */

import { formatBytes, formatDate, formatNumber } from '@/lib/format'

/** `repository.access.granted` -> `Repository access granted`. */
export function humanizeAction(action: string): string {
  const words = action.replace(/[._]/g, ' ').split(' ').filter(Boolean)
  if (words.length === 0) return action
  return words.join(' ').replace(/^./, (c) => c.toUpperCase())
}

/**
 * The noun an action acts on — `chunk_store.gc.started` -> `Chunk store`.
 *
 * Used to group entries visually. Deriving it from the code rather than listing the known
 * prefixes means an action added on the server is grouped correctly without a frontend release.
 */
export function actionSubject(action: string): string {
  const [head] = action.split('.')
  if (!head) return 'Other'
  return head.replace(/_/g, ' ').replace(/^./, (c) => c.toUpperCase())
}

/** `dryRun` / `changed_keys` -> `Dry run` / `Changed keys`. */
export function humanizeKey(key: string): string {
  return key
    .replace(/([a-z0-9])([A-Z])/g, '$1 $2')
    .replace(/[._-]/g, ' ')
    .toLowerCase()
    .replace(/^./, (c) => c.toUpperCase())
    .replace(/\b(id|ip|url|api|gc)\b/gi, (m) => m.toUpperCase())
}

export interface AuditMetadataField {
  key: string
  label: string
  /** Rendered value(s). More than one entry means the source value was a list. */
  values: string[]
  /** True when the value is an identifier or code and should be set in mono. */
  mono: boolean
}

const BYTE_KEY = /bytes$|size$/i
const ID_KEY = /(^|[._])id$|^id$|guid/i
const TIME_KEY = /(at|time|timestamp)$/i
const COUNT_KEY = /count$|^total|objects$|releases$|packs$/i

/**
 * Formats one metadata value according to what its key says it is.
 *
 * Key-driven rather than value-driven because the useful distinctions are not visible in the
 * value: `1073741824` is a byte count or a plain number depending only on what it counts, and
 * rendering the wrong one is worse than rendering neither.
 */
function formatValue(key: string, value: unknown): { text: string; mono: boolean } {
  if (value === null || value === undefined) return { text: '—', mono: false }

  if (typeof value === 'boolean') return { text: value ? 'Yes' : 'No', mono: false }

  if (typeof value === 'number') {
    if (BYTE_KEY.test(key)) return { text: formatBytes(value), mono: true }
    if (COUNT_KEY.test(key)) return { text: formatNumber(value), mono: true }
    return { text: formatNumber(value), mono: true }
  }

  if (typeof value === 'string') {
    // An ISO timestamp is unreadable and, unlike a number, unambiguous enough to detect.
    if (TIME_KEY.test(key) && !Number.isNaN(Date.parse(value))) {
      return { text: formatDate(value), mono: false }
    }
    if (ID_KEY.test(key)) return { text: value, mono: true }
    return { text: value, mono: /^[a-z0-9:_-]+$/i.test(value) && value.length < 48 }
  }

  return { text: JSON.stringify(value), mono: true }
}

/**
 * Parses the stored JSON blob into rows a definition list can render.
 *
 * Never throws: metadata is written by the server and read here, and a row that fails to parse
 * must degrade to "no detail" rather than take the surrounding table down with it.
 */
export function parseAuditMetadata(metadata: string | null | undefined): AuditMetadataField[] {
  if (!metadata) return []

  let parsed: unknown
  try {
    parsed = JSON.parse(metadata)
  } catch {
    return []
  }

  if (typeof parsed !== 'object' || parsed === null || Array.isArray(parsed)) return []

  return Object.entries(parsed as Record<string, unknown>).map(([key, value]) => {
    if (Array.isArray(value)) {
      const items = value.map((item) => formatValue(key, item))
      return {
        key,
        label: humanizeKey(key),
        values: items.map((item) => item.text),
        mono: items.some((item) => item.mono),
      }
    }

    const formatted = formatValue(key, value)
    return { key, label: humanizeKey(key), values: [formatted.text], mono: formatted.mono }
  })
}

/**
 * A one-line summary for the collapsed table row: the two or three fields that most distinguish
 * one entry of an action from another, not everything the blob happens to carry.
 */
export function summarizeMetadata(fields: AuditMetadataField[], limit = 3): string {
  return fields
    .slice(0, limit)
    .map((field) => `${field.label} ${field.values.join(', ')}`)
    .join(' · ')
}
