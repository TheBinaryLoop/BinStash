import { apiJson } from '../shared/api/http'
import { useTenantStore } from '../stores/tenant'
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

function base() {
  const t = useTenantStore()
  if (!t.currentTenantId) throw new Error('No tenant selected.')
  return `/api/tenants/${encodeURIComponent(t.currentTenantId)}/service-accounts`
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

export async function listApiKeys(serviceAccountId: string): Promise<ApiKeyInfoDto[]> {
  return await apiJson<ApiKeyInfoDto[]>(`${base()}/${encodeURIComponent(serviceAccountId)}/api-keys`, { method: 'GET' })
}

export async function createApiKey(serviceAccountId: string, dto: CreateApiKeyRequest): Promise<CreateApiKeyResponse> {
  return await apiJson<CreateApiKeyResponse>(`${base()}/${encodeURIComponent(serviceAccountId)}/api-keys`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto),
  })
}

export async function deleteApiKey(serviceAccountId: string, apiKeyId: string): Promise<void> {
  await apiJson<void>(`${base()}/${encodeURIComponent(serviceAccountId)}/api-keys/${encodeURIComponent(apiKeyId)}`, { method: 'DELETE' })
}