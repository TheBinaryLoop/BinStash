import { apiFetch } from '../shared/api/http'

export type HealthStatus = 'Healthy' | 'Degraded' | 'Unhealthy'

export type ChunkStoreHealthEntry = {
  storeId: string
  storeName: string
  status: HealthStatus
  error: string | null
  freeBytes: number
  totalBytes: number
  writeMs: number
  readMs: number
  timestamp: string
}

export type ChunkStoreHealthData = {
  updatedAtUtc: string
  ageSeconds: number
  stale: boolean
  stores: ChunkStoreHealthEntry[]
}

export type HealthCheckEntry = {
  name: string
  status: HealthStatus
  description: string | null
  exception: string | null
  duration: string
  data: Record<string, unknown>
}

export type HealthCheckResponse = {
  status: HealthStatus
  checks: HealthCheckEntry[]
}

export async function fetchHealth(): Promise<HealthCheckResponse> {
  const res = await apiFetch('/health', { method: 'GET' })
  if (!res.ok) {
    return { status: 'Unhealthy', checks: [] }
  }
  return (await res.json()) as HealthCheckResponse
}
