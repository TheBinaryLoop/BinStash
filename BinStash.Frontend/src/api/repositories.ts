import { apiJson } from '../shared/api/http'
import { useTenantStore } from '../stores/tenant'
import gql from 'graphql-tag'
import { normalizeGraphqlError, runMutation, runQuery } from '../shared/api/graphqlComposable'

export type RepositorySummaryDto = {
  id: string
  name: string
  description?: string | null
  storageClass: string
  chunker?: {
    type: string
    minChunkSize?: number | null
    avgChunkSize?: number | null
    maxChunkSize?: number | null
  } | null
}

export type CreateRepositoryDto = {
  name: string
  description?: string | null
  storageClassName: string
}

export type RepositoryConfigDto = {
  dedupeConfig: {
    chunker: string
    minChunkSize?: number | null
    avgChunkSize?: number | null
    maxChunkSize?: number | null
    shiftCount?: number | null
    boundaryCheckBytes?: number | null
  }
}

export type ReleaseSummaryDto = {
  id: string
  version: string
  createdAt: string
  notes?: string | null
  metrics?: ReleaseMetricsDto | null
  repository?: RepositorySummaryDto | null
}

export type ReleaseMetricsDto = {
  chunksInRelease: number
  newChunks: number
  totalLogicalBytes: number
  newUniqueLogicalBytes: number
  newCompressedBytes: number
  metaBytesFull: number
  componentsInRelease: number
  filesInRelease: number
  incrementalCompressionRatio: number
  incrementalDeduplicationRatio: number
  incrementalEffectiveRatio: number
  compressionSavedBytes: number
  deduplicationSavedBytes: number
  newDataPercent: number
}

export type RepositoryAccessDto = {
  subjectType: number
  subjectId: string
  role: string
  grantedAt: string
}

export type RepositoryWithReleaseStatsDto = {
  repository: RepositorySummaryDto
  releaseCount: number
  recentReleases: ReleaseSummaryDto[]
}

export type RepositoryDetailDto = {
  repository: RepositorySummaryDto
  releases: ReleaseSummaryDto[]
  totalReleaseCount: number
  latestRelease: ReleaseSummaryDto | null
  filteredReleaseCount?: number
  pageInfo: {
    hasNextPage: boolean
    hasPreviousPage?: boolean
    endCursor?: string | null
    startCursor?: string | null
  }
}

export type RepositoryReleaseListOptions = {
  search?: string
  pageSize?: number
  after?: string | null
  sortBy?: 'createdAt' | 'version'
  sortDirection?: 'ASC' | 'DESC'
  dateRange?: 'all' | '30d' | '90d'
}

function base() {
  const t = useTenantStore()
  if (!t.currentTenantId) throw new Error('No tenant selected.')
  return `/api/tenants/${encodeURIComponent(t.currentTenantId)}/repositories`
}

async function listRepositoryReleasesRest(repoId: string): Promise<ReleaseSummaryDto[]> {
  const data = await apiJson<any[]>(`${base()}/${encodeURIComponent(repoId)}/releases`, { method: 'GET' })
  return (data ?? []).map(toReleaseSummaryDto)
}

function toRepositorySummaryDto(x: any): RepositorySummaryDto {
  return {
    id: x.id,
    name: x.name,
    description: x.description ?? null,
    storageClass: x.storageClass,
    chunker: x.chunker
      ? {
          type: x.chunker.type,
          minChunkSize: x.chunker.minChunkSize ?? null,
          avgChunkSize: x.chunker.avgChunkSize ?? null,
          maxChunkSize: x.chunker.maxChunkSize ?? null,
        }
      : null,
  }
}

function toReleaseSummaryDto(x: any): ReleaseSummaryDto {
  return {
    id: x.id,
    version: x.version,
    createdAt: x.createdAt,
    notes: x.notes ?? null,
    metrics: x.metrics
      ? {
          chunksInRelease: x.metrics.chunksInRelease,
          newChunks: x.metrics.newChunks,
          totalLogicalBytes: Number(x.metrics.totalLogicalBytes ?? 0),
          newUniqueLogicalBytes: Number(x.metrics.newUniqueLogicalBytes ?? 0),
          newCompressedBytes: Number(x.metrics.newCompressedBytes ?? 0),
          metaBytesFull: x.metrics.metaBytesFull,
          componentsInRelease: x.metrics.componentsInRelease,
          filesInRelease: x.metrics.filesInRelease,
          incrementalCompressionRatio: x.metrics.incrementalCompressionRatio,
          incrementalDeduplicationRatio: x.metrics.incrementalDeduplicationRatio,
          incrementalEffectiveRatio: x.metrics.incrementalEffectiveRatio,
          compressionSavedBytes: Number(x.metrics.compressionSavedBytes ?? 0),
          deduplicationSavedBytes: Number(x.metrics.deduplicationSavedBytes ?? 0),
          newDataPercent: x.metrics.newDataPercent,
        }
      : null,
    repository: x.repository ? toRepositorySummaryDto(x.repository) : null,
  }
}

const REPOSITORY_SUMMARY_FIELDS = gql`
  fragment RepositorySummaryFields on RepositoryGql {
    id
    name
    description
    storageClass
    chunker {
      type
      minChunkSize
      avgChunkSize
      maxChunkSize
    }
  }
`

const RELEASE_SUMMARY_FIELDS = gql`
  fragment ReleaseSummaryFields on ReleaseGql {
    id
    version
    createdAt
    notes
    repoId
    metrics {
      chunksInRelease
      newChunks
      totalLogicalBytes
      newUniqueLogicalBytes
      newCompressedBytes
      metaBytesFull
      componentsInRelease
      filesInRelease
      incrementalCompressionRatio
      incrementalDeduplicationRatio
      incrementalEffectiveRatio
      compressionSavedBytes
      deduplicationSavedBytes
      newDataPercent
    }
  }
`

const LIST_REPOSITORIES_QUERY = gql`
  ${REPOSITORY_SUMMARY_FIELDS}
  query ListRepositories {
    repositories {
      nodes {
        ...RepositorySummaryFields
      }
    }
  }
`

const GET_REPOSITORY_QUERY = gql`
  ${REPOSITORY_SUMMARY_FIELDS}
  query GetRepository($id: UUID!) {
    repository(id: $id) {
      ...RepositorySummaryFields
    }
  }
`

const CREATE_REPOSITORY_MUTATION = gql`
  ${REPOSITORY_SUMMARY_FIELDS}
  mutation CreateRepository($input: CreateRepositoryInput!) {
    createRepository(input: $input) {
      ...RepositorySummaryFields
    }
  }
`

const GET_REPOSITORY_DETAIL_QUERY = gql`
  ${REPOSITORY_SUMMARY_FIELDS}
  ${RELEASE_SUMMARY_FIELDS}
  query GetRepositoryDetail(
    $id: UUID!
    $first: Int
    $after: String
    $where: ReleaseGqlFilterInput
    $order: [ReleaseGqlSortInput!]
  ) {
    repository(id: $id) {
      ...RepositorySummaryFields
      releases(first: $first, after: $after, where: $where, order: $order) {
        totalCount
        pageInfo {
          hasNextPage
          hasPreviousPage
          startCursor
          endCursor
        }
        nodes {
          ...ReleaseSummaryFields
        }
      }
      latestRelease: releases(first: 1, order: [{ createdAt: DESC }]) {
        nodes {
          ...ReleaseSummaryFields
        }
      }
      releaseStats: releases {
        totalCount
      }
    }
  }
`

const LIST_REPOSITORIES_WITH_RELEASE_STATS_QUERY = gql`
  ${REPOSITORY_SUMMARY_FIELDS}
  ${RELEASE_SUMMARY_FIELDS}
  query ListRepositoriesWithReleaseStats($releasesPerRepo: Int) {
    repositories {
      nodes {
        ...RepositorySummaryFields
        releases(order: [{ createdAt: DESC }], first: $releasesPerRepo) {
          totalCount
          nodes {
            ...ReleaseSummaryFields
          }
        }
      }
    }
  }
`

const GET_RELEASE_QUERY = gql`
  ${REPOSITORY_SUMMARY_FIELDS}
  ${RELEASE_SUMMARY_FIELDS}
  query GetRelease($id: UUID!) {
    release(id: $id) {
      ...ReleaseSummaryFields
      repository {
        ...RepositorySummaryFields
      }
    }
  }
`

export async function listRepositories(): Promise<RepositorySummaryDto[]> {
  try {
    const data = await runQuery<any>(LIST_REPOSITORIES_QUERY, undefined, { tenantScoped: true })

    return (data?.repositories?.nodes ?? []).map(toRepositorySummaryDto)
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load repositories.')
  }
}

export async function getRepository(repoId: string): Promise<RepositorySummaryDto> {
  try {
    const data = await runQuery<any>(GET_REPOSITORY_QUERY, { id: repoId }, { tenantScoped: true })

    const repo = data?.repository
    if (!repo) throw new Error('Repository not found.')
    return toRepositorySummaryDto(repo)
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load repository.')
  }
}

export async function createRepository(dto: CreateRepositoryDto): Promise<RepositorySummaryDto> {
  try {
    const data = await runMutation<any>(CREATE_REPOSITORY_MUTATION, {
      input: {
        name: dto.name,
        description: dto.description ?? null,
        storageClassName: dto.storageClassName,
      },
    }, { tenantScoped: true })

    const repo = data?.createRepository
    if (!repo) throw new Error('Repository creation failed.')
    return toRepositorySummaryDto(repo)
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not create repository.')
  }
}

export async function getRepositoryReleases(repoId: string): Promise<ReleaseSummaryDto[]> {
  try {
    const releases = await listRepositoryReleasesRest(repoId)
    return releases.sort((a, b) => Date.parse(b.createdAt) - Date.parse(a.createdAt))
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load releases.')
  }
}

export async function getRepositoryDetail(repoId: string): Promise<RepositoryDetailDto> {
  return await getRepositoryDetailWithReleases(repoId)
}

function buildReleaseOrder(options?: RepositoryReleaseListOptions): Array<Record<string, 'ASC' | 'DESC'>> {
  const sortBy = options?.sortBy ?? 'createdAt'
  const sortDirection = options?.sortDirection ?? 'DESC'

  if (sortBy === 'version') {
    return [{ version: sortDirection }, { createdAt: 'DESC' }]
  }

  return [{ createdAt: sortDirection }, { version: 'DESC' }]
}

export async function getRepositoryDetailWithReleases(
  repoId: string,
  options?: RepositoryReleaseListOptions,
): Promise<RepositoryDetailDto> {
  try {
    const pageSize = Math.max(1, options?.pageSize ?? 10)
    const search = options?.search?.trim()
    const dateFilter =
      options?.dateRange === '30d' || options?.dateRange === '90d'
        ? {
            gte: new Date(
              Date.now() - (options.dateRange === '30d' ? 30 : 90) * 24 * 60 * 60 * 1000,
            ).toISOString(),
          }
        : undefined

    const where = {
      and: [
        ...(dateFilter ? [{ createdAt: dateFilter }] : []),
        ...(search
          ? [{ or: [{ version: { contains: search } }, { notes: { contains: search } }] }]
          : []),
      ],
    }

    const detailData = await runQuery<any>(
      GET_REPOSITORY_DETAIL_QUERY,
      {
        id: repoId,
        first: pageSize,
        after: options?.after ?? null,
        where: where.and.length > 0 ? where : undefined,
        order: buildReleaseOrder(options),
      },
      { tenantScoped: true },
    )

    const repository = detailData?.repository
    if (!repository) throw new Error('Repository not found.')
    const releasesConnection = repository?.releases
    const latestRelease = repository?.latestRelease?.nodes?.[0] ?? null

    return {
      repository: toRepositorySummaryDto(repository),
      releases: (releasesConnection?.nodes ?? []).filter(Boolean).map(toReleaseSummaryDto),
      totalReleaseCount: repository?.releaseStats?.totalCount ?? 0,
      filteredReleaseCount: releasesConnection?.totalCount ?? 0,
      latestRelease: latestRelease ? toReleaseSummaryDto({ ...latestRelease, repository }) : null,
      pageInfo: {
        hasNextPage: Boolean(releasesConnection?.pageInfo?.hasNextPage),
        hasPreviousPage: Boolean(releasesConnection?.pageInfo?.hasPreviousPage),
        startCursor: releasesConnection?.pageInfo?.startCursor ?? null,
        endCursor: releasesConnection?.pageInfo?.endCursor ?? null,
      },
    }
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load repository details.')
  }
}

export async function listRepositoriesWithReleaseStats(releasesPerRepo = 10): Promise<RepositoryWithReleaseStatsDto[]> {
  try {
    const data = await runQuery<any>(
      LIST_REPOSITORIES_WITH_RELEASE_STATS_QUERY,
      { releasesPerRepo },
      { tenantScoped: true },
    )

    return (data?.repositories?.nodes ?? []).map((node: any) => {
      const repository = toRepositorySummaryDto(node)
      const releases = (node?.releases?.nodes ?? []).map((x: any) => ({
        ...toReleaseSummaryDto(x),
        repository,
      }))

      return {
        repository,
        releaseCount: node?.releases?.totalCount ?? 0,
        recentReleases: releases,
      }
    })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load repository release stats.')
  }
}

export async function getRepositoryConfig(repoId: string): Promise<RepositoryConfigDto> {
  return await apiJson<RepositoryConfigDto>(`${base()}/${encodeURIComponent(repoId)}/config`, { method: 'GET' })
}

export async function getRepositoryAccess(repoId: string): Promise<RepositoryAccessDto[]> {
  return await apiJson<RepositoryAccessDto[]>(`${base()}/${encodeURIComponent(repoId)}/access`, { method: 'GET' })
}

export async function setRepositoryAccess(repoId: string, dto: Omit<RepositoryAccessDto, 'grantedAt'>): Promise<RepositoryAccessDto> {
  return await apiJson<RepositoryAccessDto>(`${base()}/${encodeURIComponent(repoId)}/access`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto),
  })
}

export async function deleteRepositoryAccess(repoId: string, subjectType: number, subjectId: string): Promise<void> {
  const t = useTenantStore()
  if (!t.currentTenantId) throw new Error('No tenant selected.')
  const url = `/api/tenants/${encodeURIComponent(t.currentTenantId)}/repositories/${encodeURIComponent(repoId)}/access/${subjectType}/${encodeURIComponent(subjectId)}`
  await apiJson<void>(url, { method: 'DELETE' })
}

// Single-release endpoints (accept explicit tenantId for use outside tenant context)
export async function getRelease(tenantId: string, repoId: string, releaseId: string): Promise<ReleaseSummaryDto> {
  try {
    const data = await runQuery<any>(GET_RELEASE_QUERY, {
      id: releaseId,
    }, {
      tenantScoped: true,
      tenantId,
    })

    const rel = data?.release
    if (!rel) throw new Error('Release not found.')
    if (rel.repoId !== repoId) throw new Error('Release not found in repository.')
    return toReleaseSummaryDto(rel)
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load release.')
  }
}

export async function getReleaseProperties(tenantId: string, repoId: string, releaseId: string): Promise<string> {
  return await apiJson<string>(
    `/api/tenants/${encodeURIComponent(tenantId)}/repositories/${encodeURIComponent(repoId)}/releases/${encodeURIComponent(releaseId)}/properties`,
    { method: 'GET' },
  )
}
