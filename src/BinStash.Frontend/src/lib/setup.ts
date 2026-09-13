import { apiJson, apiPost } from './http'

/**
 * The setup wizard talks to `/api/setup`, which is deliberately REST: it runs before
 * GraphQL authorization exists (the session is a short-lived "Setup" cookie obtained by
 * claiming the one-time code the server prints to its log on first start).
 */

export type SetupStep =
  | 'Claim'
  | 'Tenancy'
  | 'DefaultTenant'
  | 'ChunkStore'
  | 'StorageClass'
  | 'StorageClassDefaultMappings'
  | 'InstanceAdmin'
  | 'Review'
  | 'Done'

export interface SetupChunkStore {
  id: string
  name: string
  type: string
}

export interface SetupStatus {
  isInitialized: boolean
  currentStep?: SetupStep
  setupVersion?: number
  data?: {
    tenancyMode?: string | null
    chunkStores: SetupChunkStore[]
    storageClasses: Array<{ name: string; displayName: string; description?: string | null }>
    storageClassDefaultMappings: Array<{
      storageClassName: string
      chunkStoreId: string
      isDefault: boolean
      isEnabled: boolean
    }>
    tenants: Array<{ id: string; name: string; slug: string }>
    instanceAdmins: Array<{ id: string; email: string }>
    tenantAdmins: Array<{ id: string; email: string }>
  }
}

export const setupApi = {
  status: () => apiJson<SetupStatus>('/api/setup/status'),

  claim: (code: string) => apiPost('/api/setup/claim', { code }),

  setTenancy: (mode: 'Single' | 'Multi') => apiPost('/api/setup/tenancy', { mode }),

  configureDefaultTenant: (name: string, slug: string) =>
    apiPost('/api/setup/default-tenant', { name, slug }),

  chunkStoreTypes: () => apiJson<Array<{ name: string; value: number }>>(
    '/api/setup/chunk-stores/enabled-types',
  ),

  /**
   * `type` must be the NUMERIC enum value. The endpoint binds a `ChunkStoreType` enum and
   * the server's JSON options have no string-enum converter, so sending "Local" fails to
   * deserialize. That is why /chunk-stores/enabled-types reports `value` alongside `name`.
   */
  ensureChunkStore: (input: { type: number; name: string; localPath: string; skip?: boolean }) =>
    apiPost('/api/setup/chunk-stores', {
      type: input.type,
      name: input.name,
      localPath: input.localPath,
      skip: input.skip ?? false,
    }),

  ensureStorageClasses: (
    storageClasses: Array<{ name: string; displayName: string; description?: string }>,
  ) => apiPost('/api/setup/storage-class', { storageClasses }),

  ensureStorageDefaults: (
    mappings: Array<{
      chunkStoreId: string
      storageClassName: string
      isDefault: boolean
      isEnabled: boolean
    }>,
  ) => apiPost('/api/setup/storage/defaults', { storageClassDefaultMappings: mappings }),

  createAdmin: (input: {
    isTenantAdmin: boolean
    isInstanceAdmin: boolean
    email: string
    password: string
    firstName?: string
    lastName?: string
  }) => apiPost('/api/setup/admin', input),

  finish: () => apiPost('/api/setup/finish', {}),
}

/** The default storage classes a fresh instance is seeded with. */
export const DEFAULT_STORAGE_CLASSES = [
  {
    name: 'standard',
    displayName: 'Standard',
    description: 'General-purpose storage for build artifacts.',
  },
] as const
