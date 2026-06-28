import { apiJson } from '../shared/api/http'
import { createStorageClasses as setupCreateStorageClasses, createStorageDefaults } from '../features/setup/api/setup.api'

export type StorageClassDto = {
  name: string
  displayName: string
  description?: string | null
  isDeprecated: boolean
}

export type StorageClassDefaultMappingDto = {
  storageClassName: string
  chunkStoreId: string
  isDefault: boolean
  isEnabled: boolean
}

/**
 * Lists all storage classes via the setup status endpoint.
 */
export async function listStorageClasses(): Promise<StorageClassDto[]> {
  const status = await apiJson<StorageClassDto[]>('/api/storage-classes/')
  return status ?? []
}

/**
 * Lists all storage class default mappings via the setup status endpoint.
 */
export async function listStorageDefaultMappings(): Promise<StorageClassDefaultMappingDto[]> {
  const status = await apiJson<StorageClassDefaultMappingDto[]>('/api/storage-classes/default-mappings')
  return status ?? []
}

/**
 * Creates (or ensures) a set of storage classes.
 */
export async function createStorageClasses(
  storageClasses: StorageClassDto[]
): Promise<void> {
  await setupCreateStorageClasses(storageClasses)
}

/**
 * Saves the storage class default mappings.
 */
export async function saveStorageDefaultMappings(
  mappings: StorageClassDefaultMappingDto[]
): Promise<void> {
  await apiJson<unknown>(`/api/storage-classes/default-mappings`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ storageClassDefaultMappings: mappings }),
  })
}