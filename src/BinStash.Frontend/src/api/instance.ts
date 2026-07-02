import gql from 'graphql-tag'
import { normalizeGraphqlError, runMutation, runQuery } from '../shared/api/graphqlComposable'

export type InstanceStats = {
  userCount: number
  tenantCount: number
  repositoryCount: number
}

const INSTANCE_STATS_QUERY = gql`
  query InstanceStats {
    instanceStats {
      userCount
      tenantCount
      repositoryCount
    }
  }
`

export async function fetchInstanceStats(): Promise<InstanceStats> {
  try {
    const data = await runQuery<{ instanceStats: InstanceStats }>(INSTANCE_STATS_QUERY)
    return data.instanceStats
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load instance statistics.')
  }
}

// ── Public URL / Domain configuration ───────────────────────────────────────

export type InstanceDomainConfig = {
  /** Public base URL used in links (emails, redirects, invite URLs, etc.). */
  baseUrl: string
}

const DOMAIN_CONFIG_QUERY = gql`
  query DomainConfig {
    domainConfig {
      baseUrl
    }
  }
`

const SET_DOMAIN_CONFIG_MUTATION = gql`
  mutation SetDomainConfig($input: SetDomainConfigInput!) {
    setDomainConfig(input: $input) {
      baseUrl
    }
  }
`

/** Reads the current instance domain/base URL configuration. */
export async function fetchInstanceDomainConfig(): Promise<InstanceDomainConfig> {
  try {
    const data = await runQuery<{ domainConfig: { baseUrl?: string | null } }>(DOMAIN_CONFIG_QUERY)
    return { baseUrl: data.domainConfig?.baseUrl ?? '' }
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load domain configuration.')
  }
}

/** Saves the instance domain/base URL configuration. */
export async function saveInstanceDomainConfig(config: InstanceDomainConfig): Promise<void> {
  try {
    await runMutation(SET_DOMAIN_CONFIG_MUTATION, { input: { baseUrl: config.baseUrl } })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not save domain configuration.')
  }
}

// ── Tenancy configuration ─────────────────────────────────────────────────────

export type TenancyMode = 'Single' | 'Multi'

export type TenancyConfig = {
  mode: TenancyMode
  defaultTenantId?: string
}

const TENANCY_CONFIG_QUERY = gql`
  query TenancyConfig {
    tenancyConfig {
      mode
      defaultTenantId
    }
  }
`

const SET_TENANCY_CONFIG_MUTATION = gql`
  mutation SetTenancyConfig($input: SetTenancyConfigInput!) {
    setTenancyConfig(input: $input) {
      mode
      defaultTenantId
    }
  }
`

/** Reads the current tenancy configuration. */
export async function fetchTenancyConfig(): Promise<TenancyConfig> {
  try {
    const data = await runQuery<{ tenancyConfig: { mode?: string | null; defaultTenantId?: string | null } }>(
      TENANCY_CONFIG_QUERY,
    )
    const mode = data.tenancyConfig?.mode === 'Single' ? 'Single' : 'Multi'
    return { mode, defaultTenantId: data.tenancyConfig?.defaultTenantId ?? undefined }
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load tenancy configuration.')
  }
}

/** Saves tenancy mode (and optional default tenant). */
export async function saveTenancyConfig(config: TenancyConfig): Promise<void> {
  try {
    await runMutation(SET_TENANCY_CONFIG_MUTATION, {
      input: { mode: config.mode, defaultTenantId: config.defaultTenantId ?? null },
    })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not save tenancy configuration.')
  }
}

// ── Email configuration ───────────────────────────────────────────────────────

/**
 * Sentinel value returned by the server for sensitive fields (e.g. API keys,
 * passwords). The frontend shows it as "a value is saved" and sends it back
 * unchanged to preserve the existing secret on the server.
 */
export const MASKED_VALUE = '****'

export type EmailProvider = 'brevo' | 'smtp'
export type SmtpSecurityMode = 'none' | 'starttls' | 'ssl'

/** Shared settings independent of provider. */
export type SharedEmailConfig = {
  /** The address used in the "From" header of outgoing email. */
  fromEmail: string
  /** Support / reply-to address shown in email footers. */
  supportEmail: string
}

/** Brevo-specific config. `apiKey` will equal MASKED_VALUE when loaded from server. */
export type BrevoEmailConfig = {
  apiKey: string
}

/** SMTP-specific config. `password` will equal MASKED_VALUE when loaded from server. */
export type SmtpEmailConfig = {
  host: string
  port: number
  username: string
  password: string
  security: SmtpSecurityMode
}

/**
 * Full email configuration as managed by the frontend.
 * Both `brevo` and `smtp` sub-objects are always present so form state is
 * preserved when the user switches providers without saving.
 */
export type EmailConfig = {
  provider: EmailProvider | null
  shared: SharedEmailConfig
  brevo: BrevoEmailConfig
  smtp: SmtpEmailConfig
}

/** Payload used when saving email config. Only the active provider block is required. */
export type EmailConfigSavePayload = {
  provider: EmailProvider | null
  shared: SharedEmailConfig
  brevo?: BrevoEmailConfig
  smtp?: SmtpEmailConfig
}

/** Produces a blank/default EmailConfig. */
export function defaultEmailConfig(): EmailConfig {
  return {
    provider: null,
    shared: { fromEmail: '', supportEmail: '' },
    brevo: { apiKey: '' },
    smtp: { host: '', port: 587, username: '', password: '', security: 'starttls' },
  }
}

// ── SSO Types ────────────────────────────────────────────────────────────────

export type SSOProvider = 'ldap' | 'oidc' | 'entra' | 'google' | 'github'

/** A single LDAP group → role mapping entry. */
export type SSOLDAPGroupMapping = {
  /** LDAP group DN, e.g. cn=admins,dc=example,dc=com */
  groupDN: string
  /**
   * Target tenant ID.
   * Leave empty in single-tenant mode (the default tenant is used implicitly).
   * Required in multi-tenant mode for TenantAdmin / TenantMember mappings.
   */
  tenantId: string
}

export type SSOLDAPPermissionMapping = {
  /** Members of these LDAP groups receive the InstanceAdmin role. */
  instanceAdminGroups: string[]
  /** Members of these groups receive TenantAdmin for the specified tenant. */
  tenantAdminMappings: SSOLDAPGroupMapping[]
  /** Members of these groups receive TenantMember for the specified tenant. */
  tenantMemberMappings: SSOLDAPGroupMapping[]
}

export type SSOLDAPConfig = {
  url: string
  bindDN: string
  bindPassword: string
  baseDN: string
  userFilter: string
  permissionMapping: SSOLDAPPermissionMapping
}

export type SSOOIDCConfig = {
  issuer: string
  clientId: string
  clientSecret: string
  redirectUri: string
  scopes: string
}

export type SSOEntraConfig = {
  tenantId: string
  clientId: string
  clientSecret: string
  redirectUri: string
}

export type SSOGoogleConfig = {
  clientId: string
  clientSecret: string
  redirectUri: string
}

export type SSOGitHubConfig = {
  clientId: string
  clientSecret: string
  redirectUri: string
}

export type SSOConfig = {
  provider: SSOProvider | null
  ldap: SSOLDAPConfig
  oidc: SSOOIDCConfig
  entra: SSOEntraConfig
  google: SSOGoogleConfig
  github: SSOGitHubConfig
}

export type TestSSOResult = {
  success: boolean
  providerError?: string
}

/** Produces a blank/default SSOConfig. */
export function defaultSSOConfig(): SSOConfig {
  return {
    provider: null,
    ldap: {
      url: '',
      bindDN: '',
      bindPassword: '',
      baseDN: '',
      userFilter: '',
      permissionMapping: {
        instanceAdminGroups: [],
        tenantAdminMappings: [],
        tenantMemberMappings: [],
      },
    },
    oidc: {
      issuer: '',
      clientId: '',
      clientSecret: '',
      redirectUri: '',
      scopes: '',
    },
    entra: {
      tenantId: '',
      clientId: '',
      clientSecret: '',
      redirectUri: '',
    },
    google: {
      clientId: '',
      clientSecret: '',
      redirectUri: '',
    },
    github: {
      clientId: '',
      clientSecret: '',
      redirectUri: '',
    },
  }
}

/** Returns the current SSO config. Falls back to a default if none is saved. */
export async function fetchSSOConfig(): Promise<SSOConfig> {
  // Replace with actual API call
  return defaultSSOConfig()
}

/** Saves (creates or replaces) the instance-wide SSO configuration. */
export async function saveSSOConfig(config: SSOConfig): Promise<void> {
  // Replace with actual API call
  return
}

/**
 * Tests the currently-saved SSO configuration (e.g. LDAP bind, OAuth discovery).
 * Returns { success, providerError? } rather than throwing so callers can
 * distinguish transport errors from configuration errors.
 */
export async function testSSOConnection(): Promise<TestSSOResult> {
  // Replace with actual API call
  return { success: true }
}

// ── Server ↔ frontend mapping ─────────────────────────────────────────────────
// The server uses PascalCase keys and a flat envelope. These helpers convert
// between that shape and the camelCase frontend types above.

function capitalize(s: string): string {
  return s.charAt(0).toUpperCase() + s.slice(1)
}

function deserializeEmailConfig(raw: any): EmailConfig {
  const providerStr: string = raw?.provider ?? 'None'
  const provider: EmailProvider | null =
    !providerStr || providerStr === 'None' ? null : (providerStr.toLowerCase() as EmailProvider)

  return {
    provider,
    shared: {
      fromEmail: raw?.shared?.fromEmail ?? '',
      supportEmail: raw?.shared?.supportEmail ?? '',
    },
    brevo: {
      apiKey: raw?.brevo?.apiKey ?? '',
    },
    smtp: {
      host: raw?.smtp?.host ?? '',
      port: raw?.smtp?.port ?? 587,
      username: raw?.smtp?.username ?? '',
      password: raw?.smtp?.password ?? '',
      security: ((raw?.smtp?.security as string)?.toLowerCase() ?? 'starttls') as SmtpSecurityMode,
    },
  }
}

function serializeEmailConfig(config: EmailConfigSavePayload): object {
  const input: Record<string, unknown> = {
    provider: config.provider ? capitalize(config.provider) : 'None',
    shared: {
      fromEmail: config.shared.fromEmail,
      supportEmail: config.shared.supportEmail,
    },
  }

  if (config.provider === 'brevo' && config.brevo) {
    input.brevo = { apiKey: config.brevo.apiKey }
  }

  if (config.provider === 'smtp' && config.smtp) {
    input.smtp = {
      host: config.smtp.host,
      port: config.smtp.port,
      username: config.smtp.username,
      password: config.smtp.password,
      security: config.smtp.security,
    }
  }

  return input
}

// ── API functions ─────────────────────────────────────────────────────────────

const EMAIL_CONFIG_QUERY = gql`
  query EmailConfig {
    emailConfig {
      provider
      shared {
        fromEmail
        supportEmail
      }
      brevo {
        apiKey
      }
      smtp {
        host
        port
        username
        password
        security
      }
    }
  }
`

const SET_EMAIL_CONFIG_MUTATION = gql`
  mutation SetEmailConfig($input: SetEmailConfigInput!) {
    setEmailConfig(input: $input) {
      provider
    }
  }
`

/** Returns the current email config. Falls back to a default if none is saved. */
export async function fetchEmailConfig(): Promise<EmailConfig> {
  try {
    const data = await runQuery<{ emailConfig: any }>(EMAIL_CONFIG_QUERY)
    return deserializeEmailConfig(data.emailConfig)
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not load email configuration.')
  }
}

/** Saves (creates or replaces) the instance-wide email configuration. */
export async function saveEmailConfig(config: EmailConfigSavePayload): Promise<void> {
  try {
    await runMutation(SET_EMAIL_CONFIG_MUTATION, { input: serializeEmailConfig(config) })
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not save email configuration.')
  }
}

export type TestEmailResult = {
  /** Whether the provider accepted the message. */
  success: boolean
  /** Provider-returned error message when success is false. */
  providerError?: string
}

/**
 * Sends a test email via the currently saved provider config.
 * The backend returns { success, providerError? } rather than throwing for
 * provider-level failures, so callers can distinguish transport errors from
 * API / auth errors.
 */
const SEND_TEST_EMAIL_MUTATION = gql`
  mutation SendTestEmail($recipientEmail: String!) {
    sendTestEmail(recipientEmail: $recipientEmail) {
      success
      providerError
    }
  }
`

export async function sendTestEmail(recipientEmail: string): Promise<TestEmailResult> {
  try {
    const data = await runMutation<{ sendTestEmail: TestEmailResult }>(SEND_TEST_EMAIL_MUTATION, {
      recipientEmail,
    })
    return data.sendTestEmail
  } catch (e: any) {
    throw normalizeGraphqlError(e, 'Could not send test email.')
  }
}