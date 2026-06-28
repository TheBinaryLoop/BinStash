import { apiJson } from '../shared/api/http'
import type { TenantSummaryDto } from '../stores/tenant'
import { useTenantStore } from '../stores/tenant'
import gql from 'graphql-tag'
import { normalizeGraphqlError, runMutation, runQuery } from '../shared/api/graphqlComposable'
import {
  defaultSSOConfig,
  type SSOConfig,
  type TestSSOResult,
} from './instance'

export type StorageClassDto = {
  name: string
  description?: string | null
  isDefault: boolean
}

// ── InstanceAdmin tenant CRUD ─────────────────────────────────────────────────

export type CreateTenantDto = {
  name: string
  slug: string
}

export type UpdateTenantDto = {
  name: string
  slug: string
}

export type TenantMemberDto = {
  id: string
  email: string
  firstName?: string | null
  lastName?: string | null
  roles: string[]
  joinedAt?: string | null
}

export type InviteTenantMemberDto = {
  email: string
  roles: string[]
}

export type UpdateTenantMemberRolesDto = {
  roles: string[]
}

function tenantBase(tenantId?: string) {
  const t = useTenantStore()
  const id = tenantId ?? t.currentTenantId
  if (!id) throw new Error('No tenant selected.')
  return `/api/tenants/${encodeURIComponent(id)}`
}

function toTenantSummaryDto(x: any): TenantSummaryDto {
  const roles = Array.isArray(x?.myRoles) ? x.myRoles.filter((role: unknown): role is string => typeof role === 'string') : []
  const role = roles.includes('TenantAdmin')
    ? 'TenantAdmin'
    : roles.includes('TenantBillingAdmin')
      ? 'TenantBillingAdmin'
      : roles[0] ?? 'TenantMember'

  return {
    tenantId: x.id,
    name: x.name,
    slug: x.slug ?? undefined,
    role,
  }
}

const LIST_TENANTS_QUERY = gql`
  query ListTenantsForMember {
    tenants(order: [{ name: ASC }]) {
      nodes {
        id
        name
        slug
        myRoles
      }
    }
  }
`

const GET_TENANT_QUERY = gql`
  query GetTenant($id: UUID!) {
    tenant(id: $id) {
      id
      name
      slug
      myRoles
    }
  }
`

const CREATE_TENANT_MUTATION = gql`
  mutation CreateTenant($input: CreateTenantInput!) {
    createTenant(input: $input) {
      id
      name
      slug
      myRoles
    }
  }
`

const UPDATE_TENANT_MUTATION = gql`
  mutation UpdateTenant($input: UpdateTenantInput!) {
    updateTenant(input: $input) {
      id
      name
      slug
      myRoles
    }
  }
`

export async function listTenantsForMember(): Promise<TenantSummaryDto[]> {
  try {
    const data = await runQuery<any>(LIST_TENANTS_QUERY)
    return (data?.tenants?.nodes ?? []).map(toTenantSummaryDto)
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to load tenants.')
  }
}

export async function getTenant(tenantId: string): Promise<TenantSummaryDto> {
  try {
    const data = await runQuery<any>(GET_TENANT_QUERY, { id: tenantId })
    if (!data?.tenant) throw new Error('Tenant not found.')
    return toTenantSummaryDto(data.tenant)
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to load tenant.')
  }
}

// Self-service / SaaS: create a tenant as the current authenticated user.
export async function createTenant(dto: CreateTenantDto): Promise<TenantSummaryDto> {
  try {
    const data = await runMutation<any>(CREATE_TENANT_MUTATION, { input: dto })
    if (!data?.createTenant) throw new Error('Tenant creation failed.')
    return toTenantSummaryDto(data.createTenant)
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to create tenant.')
  }
}

// Preview an invitation (public, no auth required) to show tenant + role info.
export type InvitationPreviewDto = {
  tenantId: string
  tenantName: string
  tenantSlug?: string | null
  role: string
  invitedEmail?: string | null
  expiresAt?: string | null
}

export async function previewTenantInvitation(tenantId: string, invitationCode: string): Promise<InvitationPreviewDto> {
  return await apiJson<InvitationPreviewDto>(
    `/api/tenants/${encodeURIComponent(tenantId)}/invitations/${encodeURIComponent(invitationCode)}/preview`,
    { method: 'GET' },
  )
}

// Accept an invitation code for the current authenticated user.
export async function acceptTenantInvitation(tenantId: string, invitationCode: string): Promise<void> {
  await apiJson<void>(
    `/api/tenants/${encodeURIComponent(tenantId)}/invitations/${encodeURIComponent(invitationCode)}/accept`,
    { method: 'GET' },
  )
}

// InstanceAdmin: create a new tenant
export async function adminCreateTenant(dto: CreateTenantDto): Promise<TenantSummaryDto> {
  return await createTenant(dto)
}

// InstanceAdmin: update tenant name / slug
export async function adminUpdateTenant(tenantId: string, dto: UpdateTenantDto): Promise<void> {
  try {
    await runMutation<any>(UPDATE_TENANT_MUTATION, {
      input: {
        tenantId,
        name: dto.name,
        slug: dto.slug,
      },
    })
  } catch (error) {
    throw normalizeGraphqlError(error, 'Failed to update tenant.')
  }
}

// InstanceAdmin: delete a tenant
export async function adminDeleteTenant(tenantId: string): Promise<void> {
  await apiJson<void>(`/api/tenants/${encodeURIComponent(tenantId)}`, { method: 'DELETE' })
}

export async function listStorageClasses(): Promise<StorageClassDto[]> {
  return await apiJson<StorageClassDto[]>(`${tenantBase()}/storage-classes`, { method: 'GET' })
}

// Members
export async function listMembers(): Promise<TenantMemberDto[]> {
  return await apiJson<TenantMemberDto[]>(`${tenantBase()}/members`, { method: 'GET' })
}

export async function inviteMember(dto: InviteTenantMemberDto): Promise<void> {
  await apiJson<void>(`${tenantBase()}/invitations`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto),
  })
}

export async function updateMemberRoles(memberId: string, dto: UpdateTenantMemberRolesDto): Promise<void> {
  await apiJson<void>(`${tenantBase()}/members/${encodeURIComponent(memberId)}`, {
    method: 'PATCH',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(dto),
  })
}

export async function removeMember(memberId: string): Promise<void> {
  await apiJson<void>(`${tenantBase()}/members/${encodeURIComponent(memberId)}`, { method: 'DELETE' })
}

export async function leaveTenant(): Promise<void> {
  await apiJson<void>(`${tenantBase()}/leave`, { method: 'POST' })
}

// ── Tenant-scoped SSO configuration ───────────────────────────────────────────

/**
 * Returns the current tenant SSO config.
 * TODO: wire to backend endpoint when available.
 */
export async function fetchTenantSSOConfig(): Promise<SSOConfig> {
  // Placeholder until backend endpoint exists
  return defaultSSOConfig()
}

/**
 * Saves (creates or replaces) the tenant-scoped SSO configuration.
 * TODO: wire to backend endpoint when available.
 */
export async function saveTenantSSOConfig(_config: SSOConfig): Promise<void> {
  // Placeholder until backend endpoint exists
  return
}

/**
 * Tests the currently-saved tenant SSO configuration.
 * TODO: wire to backend endpoint when available.
 */
export async function testTenantSSOConnection(): Promise<TestSSOResult> {
  // Placeholder until backend endpoint exists
  return { success: true }
}