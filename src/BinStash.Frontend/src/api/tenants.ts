import type { TenantSummaryDto } from '../stores/tenant'
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

const TENANT_INVITATION_PREVIEW_QUERY = gql`
  query TenantInvitationPreview($tenantId: UUID!, $code: String!) {
    tenantInvitationPreview(tenantId: $tenantId, code: $code) {
      tenantId
      tenantName
      tenantSlug
      role
      invitedEmail
      expiresAt
    }
  }
`

const ACCEPT_TENANT_INVITATION_MUTATION = gql`
  mutation AcceptTenantInvitation($tenantId: UUID!, $code: String!) {
    acceptTenantInvitation(tenantId: $tenantId, code: $code)
  }
`

export async function previewTenantInvitation(tenantId: string, invitationCode: string): Promise<InvitationPreviewDto> {
  try {
    const data = await runQuery<{ tenantInvitationPreview: InvitationPreviewDto | null }>(
      TENANT_INVITATION_PREVIEW_QUERY,
      { tenantId, code: invitationCode },
    )
    if (!data.tenantInvitationPreview) throw new Error('Invitation not found or expired.')
    return data.tenantInvitationPreview
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load invitation.')
  }
}

// Accept an invitation code for the current authenticated user.
export async function acceptTenantInvitation(tenantId: string, invitationCode: string): Promise<void> {
  try {
    await runMutation(ACCEPT_TENANT_INVITATION_MUTATION, { tenantId, code: invitationCode })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not accept invitation.')
  }
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

const DELETE_TENANT_MUTATION = gql`
  mutation DeleteTenant($tenantId: UUID!) {
    deleteTenant(tenantId: $tenantId)
  }
`

const TENANT_STORAGE_CLASSES_QUERY = gql`
  query TenantStorageClasses {
    tenantStorageClasses {
      name
      description
      isDefault
    }
  }
`

const TENANT_MEMBERS_QUERY = gql`
  query TenantMembers {
    tenantMembers {
      id
      email
      firstName
      lastName
      roles
      joinedAt
    }
  }
`

const INVITE_TENANT_MEMBER_MUTATION = gql`
  mutation InviteTenantMember($input: InviteTenantMemberInput!) {
    inviteTenantMember(input: $input)
  }
`

const UPDATE_TENANT_MEMBER_ROLES_MUTATION = gql`
  mutation UpdateTenantMemberRoles($memberId: UUID!, $roles: [String!]!) {
    updateTenantMemberRoles(memberId: $memberId, roles: $roles) {
      id
    }
  }
`

const REMOVE_TENANT_MEMBER_MUTATION = gql`
  mutation RemoveTenantMember($memberId: UUID!) {
    removeTenantMember(memberId: $memberId)
  }
`

const LEAVE_TENANT_MUTATION = gql`
  mutation LeaveTenant {
    leaveTenant
  }
`

// InstanceAdmin: delete a tenant
export async function adminDeleteTenant(tenantId: string): Promise<void> {
  try {
    await runMutation(DELETE_TENANT_MUTATION, { tenantId })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not delete tenant.')
  }
}

export async function listStorageClasses(): Promise<StorageClassDto[]> {
  try {
    const data = await runQuery<{ tenantStorageClasses: StorageClassDto[] }>(
      TENANT_STORAGE_CLASSES_QUERY,
      undefined,
      { tenantScoped: true },
    )
    return data.tenantStorageClasses ?? []
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load storage classes.')
  }
}

// Members
export async function listMembers(): Promise<TenantMemberDto[]> {
  try {
    const data = await runQuery<{ tenantMembers: TenantMemberDto[] }>(
      TENANT_MEMBERS_QUERY,
      undefined,
      { tenantScoped: true },
    )
    return data.tenantMembers ?? []
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load tenant members.')
  }
}

export async function inviteMember(dto: InviteTenantMemberDto): Promise<void> {
  try {
    await runMutation(INVITE_TENANT_MEMBER_MUTATION, { input: { email: dto.email, roles: dto.roles } }, { tenantScoped: true })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not invite member.')
  }
}

export async function updateMemberRoles(memberId: string, dto: UpdateTenantMemberRolesDto): Promise<void> {
  try {
    await runMutation(UPDATE_TENANT_MEMBER_ROLES_MUTATION, { memberId, roles: dto.roles }, { tenantScoped: true })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not update member roles.')
  }
}

export async function removeMember(memberId: string): Promise<void> {
  try {
    await runMutation(REMOVE_TENANT_MEMBER_MUTATION, { memberId }, { tenantScoped: true })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not remove member.')
  }
}

export async function leaveTenant(): Promise<void> {
  try {
    await runMutation(LEAVE_TENANT_MUTATION, undefined, { tenantScoped: true })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not leave tenant.')
  }
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