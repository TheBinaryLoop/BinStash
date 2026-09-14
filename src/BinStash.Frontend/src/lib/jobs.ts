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

/**
 * Human-readable names for the collection phases.
 *
 * The server's phase values are internal identifiers. "Mark" and "Resolve" in particular say
 * nothing to an operator about what the run is spending time on, which matters because they are
 * the two longest phases and the ones most likely to be watched.
 */
const GC_PHASE_LABELS: Record<string, string> = {
  Pending: 'Starting',
  Snapshot: 'Taking a snapshot',
  Mark: 'Walking releases',
  Resolve: 'Resolving files',
  Sweep: 'Finding unreachable content',
  Reclaim: 'Reclaiming space',
  Completed: 'Completed',
}

export function gcPhaseLabel(phase: string): string {
  return GC_PHASE_LABELS[phase] ?? phase
}
