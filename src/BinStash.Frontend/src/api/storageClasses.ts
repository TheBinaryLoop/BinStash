import gql from 'graphql-tag'
import { normalizeGraphqlError, runMutation, runQuery } from '../shared/api/graphqlComposable'
import { createStorageClasses as setupCreateStorageClasses } from '../features/setup/api/setup.api'

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

const STORAGE_CLASSES_QUERY = gql`
  query StorageClasses {
    storageClasses {
      name
      displayName
      description
      isDeprecated
    }
  }
`

const STORAGE_CLASS_DEFAULT_MAPPINGS_QUERY = gql`
  query StorageClassDefaultMappings {
    storageClassDefaultMappings {
      storageClassName
      chunkStoreId
      isDefault
      isEnabled
    }
  }
`

const SET_STORAGE_CLASS_DEFAULT_MAPPINGS_MUTATION = gql`
  mutation SetStorageClassDefaultMappings($input: SetStorageClassDefaultMappingsInput!) {
    setStorageClassDefaultMappings(input: $input)
  }
`

/**
 * Lists all storage classes.
 */
export async function listStorageClasses(): Promise<StorageClassDto[]> {
  try {
    const data = await runQuery<{ storageClasses: StorageClassDto[] }>(STORAGE_CLASSES_QUERY)
    return data.storageClasses ?? []
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load storage classes.')
  }
}

/**
 * Lists all storage class default mappings.
 */
export async function listStorageDefaultMappings(): Promise<StorageClassDefaultMappingDto[]> {
  try {
    const data = await runQuery<{ storageClassDefaultMappings: StorageClassDefaultMappingDto[] }>(
      STORAGE_CLASS_DEFAULT_MAPPINGS_QUERY,
    )
    return data.storageClassDefaultMappings ?? []
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load storage class default mappings.')
  }
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
 * Saves the storage class default mappings (full replace).
 */
export async function saveStorageDefaultMappings(
  mappings: StorageClassDefaultMappingDto[]
): Promise<void> {
  try {
    await runMutation(SET_STORAGE_CLASS_DEFAULT_MAPPINGS_MUTATION, { input: { mappings } })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not save storage class default mappings.')
  }
}