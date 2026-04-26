import { apiJson } from '../shared/api/http'
import gql from 'graphql-tag'
import { normalizeGraphqlError, runMutation, runQuery } from '../shared/api/graphqlComposable'
import type { BackgroundJobDto } from './backgroundJobs'

export type ChunkStoreSummaryDto = {
  id: string
  name: string
}

export type ChunkStoreChunkerDto = {
  type: string
  minChunkSize?: number | null
  avgChunkSize?: number | null
  maxChunkSize?: number | null
}

export type ChunkStoreBackendSettingsDto = {
  type: string
  localPath?: string | null
}

export type ChunkStoreDetailDto = {
  id: string
  name: string
  type: string
  chunker: ChunkStoreChunkerDto
  backendSettings?: ChunkStoreBackendSettingsDto | null
  stats: Record<string, unknown>
}

export type CreateChunkStoreDto = {
  name: string
  type: string
  localPath: string
  chunker?: ChunkStoreChunkerDto | null
}

export type ChunkStoreStatsDto = {
  totalChunks: number
}

const BASE = '/api/chunk-stores'

function toChunkStoreSummaryDto(x: any): ChunkStoreSummaryDto {
  return {
    id: x.id,
    name: x.name,
  }
}

const LIST_CHUNK_STORES_QUERY = gql`
  query ListChunkStores {
    chunkStores(order: [{ name: ASC }]) {
      nodes {
        id
        name
      }
    }
  }
`

const CREATE_CHUNK_STORE_MUTATION = gql`
  mutation CreateChunkStore($input: CreateChunkStoreInput!) {
    createChunkStore(input: $input) {
      id
      name
      type
      backendSettings {
        backendType
        localPath
      }
    }
  }
`

const REBUILD_CHUNK_STORE_MUTATION = gql`
  mutation RebuildChunkStore($chunkStoreId: UUID!) {
    rebuildChunkStore(chunkStoreId: $chunkStoreId) {
      id
      jobType
      status
      chunkStoreId
      createdAt
    }
  }
`

const UPGRADE_CHUNK_STORE_MUTATION = gql`
  mutation UpgradeChunkStore($chunkStoreId: UUID!) {
    upgradeChunkStore(chunkStoreId: $chunkStoreId) {
      id
      jobType
      status
      chunkStoreId
      createdAt
    }
  }
`

export async function getEnabledChunkStoreTypes() {
  return apiJson<{ name: string, value: number }[]>(`${BASE}/enabled-types`)
}

export async function listChunkStores(): Promise<ChunkStoreSummaryDto[]> {
  try {
    const data = await runQuery<any>(LIST_CHUNK_STORES_QUERY)
    return (data?.chunkStores?.nodes ?? []).map(toChunkStoreSummaryDto)
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to load chunk stores.')
  }
}

export async function getChunkStore(id: string): Promise<ChunkStoreDetailDto> {
  return await apiJson<ChunkStoreDetailDto>(`${BASE}/${encodeURIComponent(id)}`, { method: 'GET' })
}

export async function createChunkStore(dto: CreateChunkStoreDto): Promise<ChunkStoreSummaryDto> {
  try {
    const data = await runMutation<any>(CREATE_CHUNK_STORE_MUTATION, {
      input: {
        name: dto.name,
        type: dto.type,
        localPath: dto.localPath ?? null,
        chunker: dto.chunker
          ? {
              type: dto.chunker.type,
              minChunkSize: dto.chunker.minChunkSize ?? null,
              avgChunkSize: dto.chunker.avgChunkSize ?? null,
              maxChunkSize: dto.chunker.maxChunkSize ?? null,
            }
          : null,
      },
    })
    const store = data?.createChunkStore
    if (!store) throw new Error('Chunk store creation failed.')
    return toChunkStoreSummaryDto(store)
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to create chunk store.')
  }
}

export async function getChunkStoreStats(id: string): Promise<ChunkStoreStatsDto> {
  return await apiJson<ChunkStoreStatsDto>(`${BASE}/${encodeURIComponent(id)}/stats`, { method: 'GET' })
}

export async function rebuildChunkStore(id: string): Promise<void> {
  try {
    await runMutation<any>(REBUILD_CHUNK_STORE_MUTATION, { chunkStoreId: id })
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to start rebuild job.')
  }
}

export type UpgradeJobDto = BackgroundJobDto

export async function upgradeChunkStore(id: string): Promise<BackgroundJobDto> {
  try {
    const data = await runMutation<any>(UPGRADE_CHUNK_STORE_MUTATION, { chunkStoreId: id })
    const job = data?.upgradeChunkStore
    if (!job) throw new Error('Upgrade job creation failed.')
    return {
      id: job.id,
      jobType: job.jobType,
      chunkStoreId: job.chunkStoreId,
      status: job.status,
      errorDetails: null,
      createdAt: job.createdAt,
      startedAt: null,
      completedAt: null,
      upgradeProgress: null,
      rebuildProgress: null,
    } satisfies BackgroundJobDto
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to start upgrade job.')
  }
}
