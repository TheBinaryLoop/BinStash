import gql from 'graphql-tag'
import { normalizeGraphqlError, runMutation, runQuery } from '../shared/api/graphqlComposable'

export type ServiceAccountInfoDto = {
  id: string
  name: string
  createdAt: string
}

export type CreateServiceAccountDto = {
  name: string
}

export type ApiKeyInfoDto = {
  id: string
  displayName: string
  createdAt: string
  expiresAt?: string | null
  lastUsedAt?: string | null
  isActive: boolean
  scopes?: string[]
}

export type CreateApiKeyRequest = {
  displayName: string
  expiresAt?: string | null
  scopes?: string[]
}

export type CreateApiKeyResponse = {
  displayName: string
  key: string
  expiresAt?: string | null
}

function toServiceAccountInfoDto(x: any): ServiceAccountInfoDto {
  return {
    id: x.id,
    name: x.name,
    createdAt: x.createdAt,
  }
}

const LIST_SERVICE_ACCOUNTS_QUERY = gql`
  query ListServiceAccounts {
    serviceAccounts(order: [{ createdAt: DESC }]) {
      nodes {
        id
        name
        createdAt
      }
    }
  }
`

const CREATE_SERVICE_ACCOUNT_MUTATION = gql`
  mutation CreateServiceAccount($input: CreateServiceAccountInput!) {
    createServiceAccount(input: $input) {
      id
      name
      createdAt
    }
  }
`

const DELETE_SERVICE_ACCOUNT_MUTATION = gql`
  mutation DeleteServiceAccount($accountId: UUID!) {
    deleteServiceAccount(accountId: $accountId)
  }
`

export async function listServiceAccounts(): Promise<ServiceAccountInfoDto[]> {
  try {
    const data = await runQuery<any>(LIST_SERVICE_ACCOUNTS_QUERY, undefined, { tenantScoped: true })
    return (data?.serviceAccounts?.nodes ?? []).map(toServiceAccountInfoDto)
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Failed to load service accounts.')
  }
}

export async function createServiceAccount(dto: CreateServiceAccountDto): Promise<ServiceAccountInfoDto> {
  try {
    const data = await runMutation<any>(CREATE_SERVICE_ACCOUNT_MUTATION, {
      input: {
        name: dto.name,
      },
    }, { tenantScoped: true })

    const account = data?.createServiceAccount
    if (!account) throw new Error('Service account creation failed.')
    return toServiceAccountInfoDto(account)
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Failed to create service account.')
  }
}

export async function deleteServiceAccount(serviceAccountId: string): Promise<void> {
  try {
    const data = await runMutation<any>(DELETE_SERVICE_ACCOUNT_MUTATION, {
      accountId: serviceAccountId,
    }, { tenantScoped: true })

    if (data?.deleteServiceAccount !== true) {
      throw new Error('Delete service account failed.')
    }
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Failed to delete service account.')
  }
}

const SERVICE_ACCOUNT_API_KEYS_QUERY = gql`
  query ServiceAccountApiKeys($serviceAccountId: UUID!) {
    serviceAccountApiKeys(serviceAccountId: $serviceAccountId) {
      id
      displayName
      createdAt
      expiresAt
      lastUsedAt
      isActive
      scopes
    }
  }
`

const CREATE_SERVICE_ACCOUNT_API_KEY_MUTATION = gql`
  mutation CreateServiceAccountApiKey($serviceAccountId: UUID!, $input: CreateServiceAccountApiKeyInput!) {
    createServiceAccountApiKey(serviceAccountId: $serviceAccountId, input: $input) {
      displayName
      key
      expiresAt
    }
  }
`

const DELETE_SERVICE_ACCOUNT_API_KEY_MUTATION = gql`
  mutation DeleteServiceAccountApiKey($serviceAccountId: UUID!, $apiKeyId: UUID!) {
    deleteServiceAccountApiKey(serviceAccountId: $serviceAccountId, apiKeyId: $apiKeyId)
  }
`

export async function listApiKeys(serviceAccountId: string): Promise<ApiKeyInfoDto[]> {
  try {
    const data = await runQuery<{ serviceAccountApiKeys: ApiKeyInfoDto[] }>(
      SERVICE_ACCOUNT_API_KEYS_QUERY,
      { serviceAccountId },
      { tenantScoped: true },
    )
    return data.serviceAccountApiKeys ?? []
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load API keys.')
  }
}

export async function createApiKey(serviceAccountId: string, dto: CreateApiKeyRequest): Promise<CreateApiKeyResponse> {
  try {
    const data = await runMutation<{ createServiceAccountApiKey: CreateApiKeyResponse }>(
      CREATE_SERVICE_ACCOUNT_API_KEY_MUTATION,
      { serviceAccountId, input: { displayName: dto.displayName, expiresAt: dto.expiresAt ?? null, scopes: dto.scopes ?? null } },
      { tenantScoped: true },
    )
    return data.createServiceAccountApiKey
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not create API key.')
  }
}

export async function deleteApiKey(serviceAccountId: string, apiKeyId: string): Promise<void> {
  try {
    await runMutation(
      DELETE_SERVICE_ACCOUNT_API_KEY_MUTATION,
      { serviceAccountId, apiKeyId },
      { tenantScoped: true },
    )
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not delete API key.')
  }
}