import { describe, expect, it } from 'vitest'

import {
  actionSubject,
  humanizeAction,
  humanizeKey,
  parseAuditMetadata,
  summarizeMetadata,
} from '@/lib/audit'

describe('humanizeAction', () => {
  it('turns a dotted action code into a sentence', () => {
    expect(humanizeAction('repository.access.granted')).toBe('Repository access granted')
    expect(humanizeAction('chunk_store.gc.started')).toBe('Chunk store gc started')
  })

  it('returns the original when there is nothing to split', () => {
    expect(humanizeAction('')).toBe('')
  })
})

describe('actionSubject', () => {
  it('takes the noun from the first segment', () => {
    expect(actionSubject('chunk_store.gc.started')).toBe('Chunk store')
    expect(actionSubject('instance.email_config.changed')).toBe('Instance')
  })

  it('falls back rather than rendering an empty heading', () => {
    expect(actionSubject('')).toBe('Other')
  })
})

describe('humanizeKey', () => {
  it('splits camelCase and capitalises', () => {
    expect(humanizeKey('skipReclaim')).toBe('Skip reclaim')
    expect(humanizeKey('changed_keys')).toBe('Changed keys')
  })

  it('keeps initialisms upper case', () => {
    expect(humanizeKey('jobId')).toBe('Job ID')
  })
})

describe('parseAuditMetadata', () => {
  it('returns nothing for absent metadata', () => {
    expect(parseAuditMetadata(null)).toEqual([])
    expect(parseAuditMetadata(undefined)).toEqual([])
    expect(parseAuditMetadata('')).toEqual([])
  })

  it('never throws on malformed metadata', () => {
    // Written by the server and read here; a bad row must degrade to "no detail" rather
    // than take the surrounding table down.
    expect(parseAuditMetadata('{not json')).toEqual([])
    expect(parseAuditMetadata('[1,2,3]')).toEqual([])
    expect(parseAuditMetadata('"a string"')).toEqual([])
  })

  it('renders booleans as words rather than true/false', () => {
    const [field] = parseAuditMetadata(JSON.stringify({ dryRun: true }))
    expect(field.label).toBe('Dry run')
    expect(field.values).toEqual(['Yes'])
  })

  it('formats byte-valued keys as sizes', () => {
    const [field] = parseAuditMetadata(JSON.stringify({ quarantinedBytes: 1048576 }))
    expect(field.values).toEqual(['1.0 MiB'])
  })

  it('keeps list values separate instead of joining them', () => {
    const [field] = parseAuditMetadata(
      JSON.stringify({ changedKeys: ['Email:Provider', 'Email:Smtp:Host'] }),
    )
    expect(field.values).toEqual(['Email:Provider', 'Email:Smtp:Host'])
  })

  it('renders a null value as a dash', () => {
    const [field] = parseAuditMetadata(JSON.stringify({ retentionHours: null }))
    expect(field.values).toEqual(['—'])
  })

  it('marks identifiers for monospace', () => {
    const [field] = parseAuditMetadata(
      JSON.stringify({ jobId: '0195f3a0-0000-7000-8000-000000000000' }),
    )
    expect(field.mono).toBe(true)
  })
})

describe('summarizeMetadata', () => {
  it('joins the leading fields for the collapsed row', () => {
    const fields = parseAuditMetadata(
      JSON.stringify({ scheduled: true, dryRun: false, skipReclaim: false, intervalHours: 24 }),
    )
    expect(summarizeMetadata(fields)).toBe('Scheduled Yes · Dry run No · Skip reclaim No')
  })

  it('is empty when there is no metadata to summarise', () => {
    expect(summarizeMetadata([])).toBe('')
  })
})
