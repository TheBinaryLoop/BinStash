import gql from 'graphql-tag'
import { normalizeGraphqlError, runMutation, runQuery } from '../shared/api/graphqlComposable'

// ──────────────────────────────────────────────────────────────────────────────
// Types
// ──────────────────────────────────────────────────────────────────────────────

export type RebuildJobProgressDto = {
  totalBuckets: number
  processedBuckets: number
  failedBuckets: number
}

export type UpgradeJobProgressDto = {
  targetSerializerVersion: number
  totalReleases: number
  processedReleases: number
  failedReleases: number
  skippedReleases: number
  bytesSaved: number
  bytesGrown: number
}

export type BackgroundJobDto = {
  id: string
  jobType: string
  status: string
  chunkStoreId: string
  createdAt: string
  startedAt?: string | null
  completedAt?: string | null
  errorDetails?: string | null
  rebuildProgress?: RebuildJobProgressDto | null
  upgradeProgress?: UpgradeJobProgressDto | null
}

// ──────────────────────────────────────────────────────────────────────────────
// GQL fragments / queries / mutations
// ──────────────────────────────────────────────────────────────────────────────

const BACKGROUND_JOB_FIELDS = gql`
  fragment BackgroundJobFields on BackgroundJobGql {
    id
    jobType
    status
    chunkStoreId
    createdAt
    startedAt
    completedAt
    errorDetails
    rebuildProgress {
      totalBuckets
      processedBuckets
      failedBuckets
    }
    upgradeProgress {
      targetSerializerVersion
      totalReleases
      processedReleases
      failedReleases
      skippedReleases
      bytesSaved
      bytesGrown
    }
  }
`

const LIST_BACKGROUND_JOBS_QUERY = gql`
  ${BACKGROUND_JOB_FIELDS}
  query ListBackgroundJobs($jobType: String, $chunkStoreId: UUID) {
    backgroundJobs(jobType: $jobType, chunkStoreId: $chunkStoreId) {
      nodes {
        ...BackgroundJobFields
      }
      totalCount
    }
  }
`

const GET_BACKGROUND_JOB_QUERY = gql`
  ${BACKGROUND_JOB_FIELDS}
  query GetBackgroundJob($id: UUID!) {
    backgroundJob(id: $id) {
      ...BackgroundJobFields
    }
  }
`

const CANCEL_BACKGROUND_JOB_MUTATION = gql`
  ${BACKGROUND_JOB_FIELDS}
  mutation CancelBackgroundJob($jobId: UUID!) {
    cancelBackgroundJob(jobId: $jobId) {
      ...BackgroundJobFields
    }
  }
`

// ──────────────────────────────────────────────────────────────────────────────
// API functions
// ──────────────────────────────────────────────────────────────────────────────

export async function listBackgroundJobs(jobType?: string, chunkStoreId?: string): Promise<BackgroundJobDto[]> {
  try {
    const data = await runQuery<any>(LIST_BACKGROUND_JOBS_QUERY, {
      jobType: jobType ?? null,
      chunkStoreId: chunkStoreId ?? null,
    })
    return data?.backgroundJobs?.nodes ?? []
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to load background jobs.')
  }
}

export async function getBackgroundJob(id: string): Promise<BackgroundJobDto | null> {
  try {
    const data = await runQuery<any>(GET_BACKGROUND_JOB_QUERY, { id })
    return data?.backgroundJob ?? null
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to load background job.')
  }
}

export async function cancelBackgroundJob(jobId: string): Promise<BackgroundJobDto> {
  try {
    const data = await runMutation<any>(CANCEL_BACKGROUND_JOB_MUTATION, { jobId })
    const job = data?.cancelBackgroundJob
    if (!job) throw new Error('Cancel background job failed.')
    return job
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to cancel background job.')
  }
}
