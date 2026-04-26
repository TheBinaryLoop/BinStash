// src/api/setupApi.ts
import { ChunkStoreSummaryDto, CreateChunkStoreDto } from '@/api/chunkStores'
import { apiJson } from '@/shared/api/http'

const BASE = '/api/setup'

export async function claimSetupSession(code: string) {
  return apiJson<{ ok: boolean }>(`${BASE}/claim`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ code }),
  })
}

export async function getSetupStatus() {
  return apiJson<{
    isInitialized: boolean
    currentStep: string | null
    setupVersion?: string
    data: {
      tenancyMode: 'Single' | 'Multi' | null
      chunkStores: { id: string; name: string; enabled: boolean }[]
      storageClasses: { name: string; displayName: string; description?: string }[]
      storageClassDefaultMappings: { storageClassName: string; chunkStoreId: string; isDefault: boolean; isEnabled: boolean }[]
      tenants: { tenantId: string; name: string; slug: string }[]
      instanceAdmins: { id: string; email: string; firstName?: string; lastName?: string }[]
      tenantAdmins: { id: string; email: string; firstName?: string; lastName?: string }[]
    }
  }>('/api/setup/status')
}

export async function setTenancyMode(mode: 'Single' | 'Multi') {
  return apiJson<{ mode: 'Single' | 'Multi'; locked: boolean }>(`${BASE}/tenancy`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ mode }),
  })
}

export async function configureDefaultTenant(payload: { name: string; slug: string }) {
  return apiJson<unknown>('/api/setup/default-tenant', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
}

export async function getEnabledChunkStoreTypes() {
  return apiJson<{ name: string, value: number }[]>(`${BASE}/chunk-stores/enabled-types`)
}

export async function listChunkStores(): Promise<ChunkStoreSummaryDto[]> {
  return await apiJson<ChunkStoreSummaryDto[]>(`${BASE}/chunk-stores`, { method: 'GET' })
}

export async function createChunkStore(dto: CreateChunkStoreDto): Promise<ChunkStoreSummaryDto> {
  return await apiJson<ChunkStoreSummaryDto>(`${BASE}/chunk-stores/create`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto),
  })
}

export async function setChunkStoreDone() {
  return apiJson<{ chunkStoreId: string }>(`${BASE}/chunk-stores`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ skip: true }),
  })
}

export async function createStorageDefaults(
  storageClassDefaultMappings?: {
    storageClassName: string
    chunkStoreId: string
    isDefault: boolean
    isEnabled: boolean
  }[]
) {
  return apiJson<unknown>(`${BASE}/storage/defaults`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(
      storageClassDefaultMappings
        ? { storageClassDefaultMappings }
        : {}
    ),
  })
}

export async function createStorageClasses(storageClasses: { name: string; displayName: string; description?: string }[]) {
  return apiJson<unknown>(`${BASE}/storage-class`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ storageClasses }),
  })
}

export async function createAdminUser(payload: {
  isTenantAdmin: boolean
  isInstanceAdmin: boolean
  email: string
  password: string
  firstName?: string
  lastName?: string
}) {
  return apiJson<unknown>('/api/setup/admin', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(payload),
  })
}

export async function finishSetup() {
  return apiJson<{ ok: boolean }>('/api/setup/finish', {
    method: 'POST',
  })
}

export async function logoutSetup() {
  return apiJson<{ ok: boolean }>('/api/setup/logout', {
    method: 'POST',
  })
}
