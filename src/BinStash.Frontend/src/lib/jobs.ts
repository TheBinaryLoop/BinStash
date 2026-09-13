/**
 * Background job presentation.
 *
 * The server's job type is a discriminator (`ChunkStoreGc`), not a label. It was being rendered
 * verbatim, which reads as an internal identifier leaking into the UI — and "ChunkStoreGc" in
 * particular gives an operator no clue that it is the thing that deletes data.
 */

const JOB_TYPE_LABELS: Record<string, string> = {
  ChunkStoreGc: 'Garbage collection',
  ChunkStoreRebuild: 'Index rebuild',
  ReleaseUpgrade: 'Release upgrade',
}

export function jobTypeLabel(jobType: string): string {
  return JOB_TYPE_LABELS[jobType] ?? jobType.replace(/([a-z0-9])([A-Z])/g, '$1 $2')
}
