import { apiFetch, throwForStatus } from '../shared/api/http'

export type InstanceStats = {
  userCount: number
  tenantCount: number
  repositoryCount: number
}

export async function fetchInstanceStats(): Promise<InstanceStats> {
  const res = await apiFetch('/api/instance/stats', { method: 'GET' })
  await throwForStatus(res)
  return (await res.json()) as InstanceStats
}

// ── Public URL / Domain configuration ───────────────────────────────────────

export type InstanceDomainConfig = {
  /** Public base URL used in links (emails, redirects, invite URLs, etc.). */
  baseUrl: string
}

/** Reads the current instance domain/base URL configuration. */
export async function fetchInstanceDomainConfig(): Promise<InstanceDomainConfig> {
  const res = await apiFetch('/api/instance/config/domain', { method: 'GET' })
  if (res.status === 404) return { baseUrl: '' }
  await throwForStatus(res)
  return deserializeInstanceDomainConfig(await res.json())
}

/** Saves the instance domain/base URL configuration. */
export async function saveInstanceDomainConfig(config: InstanceDomainConfig): Promise<void> {
  const res = await apiFetch('/api/instance/config/domain', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(serializeInstanceDomainConfig(config)),
  })
  await throwForStatus(res)
}

// ── Tenancy configuration ─────────────────────────────────────────────────────

export type TenancyMode = 'Single' | 'Multi'

export type TenancyConfig = {
  mode: TenancyMode
  defaultTenantId?: string
}

/** Reads the current tenancy configuration via the instance config endpoint. */
export async function fetchTenancyConfig(): Promise<TenancyConfig> {
  const res = await apiFetch('/api/instance/config/tenancy', { method: 'GET' })
  if (res.status === 404) return { mode: 'Multi', defaultTenantId: undefined }
  await throwForStatus(res)
  return deserializeTenancyConfig(await res.json())
}

/** Saves tenancy mode (and optional default tenant) via the instance config endpoint. */
export async function saveTenancyConfig(config: TenancyConfig): Promise<void> {
  const res = await apiFetch('/api/instance/config/tenancy', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(serializeTenancyConfig(config)),
  })
  await throwForStatus(res)
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
  const providerStr: string = raw.Provider ?? 'None'
  const provider: EmailProvider | null =
    providerStr === 'None' ? null : (providerStr.toLowerCase() as EmailProvider)

  return {
    provider,
    shared: {
      fromEmail: raw.Shared?.FromEmail ?? '',
      supportEmail: raw.Shared?.SupportEmail ?? '',
    },
    brevo: {
      apiKey: raw.Brevo?.ApiKey ?? '',
    },
    smtp: {
      host: raw.Smtp?.Host ?? '',
      port: raw.Smtp?.Port ?? 587,
      username: raw.Smtp?.Username ?? '',
      password: raw.Smtp?.Password ?? '',
      security: ((raw.Smtp?.Security as string)?.toLowerCase() ?? 'starttls') as SmtpSecurityMode,
    },
  }
}

function serializeEmailConfig(config: EmailConfigSavePayload): object {
  const payload: Record<string, unknown> = {
    Provider: config.provider ? capitalize(config.provider) : 'None',
    Shared: {
      FromEmail: config.shared.fromEmail,
      SupportEmail: config.shared.supportEmail,
    },
  }

  if (config.provider === 'brevo' && config.brevo) {
    payload.Brevo = {
      ApiKey: config.brevo.apiKey,
    }
  }

  if (config.provider === 'smtp' && config.smtp) {
    payload.Smtp = {
      Host: config.smtp.host,
      Port: config.smtp.port,
      Username: config.smtp.username,
      Password: config.smtp.password,
      Security: config.smtp.security,
    }
  }

  return payload
}

function deserializeTenancyConfig(raw: any): TenancyConfig {
  const modeStr: string = raw.Mode ?? 'Multi'
  const mode: TenancyMode = (modeStr as TenancyMode) || 'Multi'
  const defaultTenantId: string | undefined = raw.DefaultTenantId ?? undefined

  return { mode, defaultTenantId }
}

function serializeTenancyConfig(config: TenancyConfig): object {
  return {
    Mode: config.mode,
    DefaultTenantId: config.defaultTenantId,
  }
}

function deserializeInstanceDomainConfig(raw: any): InstanceDomainConfig {
  return {
    // Be tolerant to casing while backend contracts settle.
    baseUrl:
      raw?.BaseUrl ??
      raw?.baseUrl ??
      raw?.PublicUrl ??
      raw?.publicUrl ??
      '',
  }
}

function serializeInstanceDomainConfig(config: InstanceDomainConfig): object {
  return {
    // Send both conventional casings for forward/backward compatibility.
    BaseUrl: config.baseUrl,
    baseUrl: config.baseUrl,
  }
}

// ── API functions ─────────────────────────────────────────────────────────────

/** Returns the current email config. Falls back to a default if none is saved. */
export async function fetchEmailConfig(): Promise<EmailConfig> {
  const res = await apiFetch('/api/instance/config/email', { method: 'GET' })
  if (res.status === 404) return defaultEmailConfig()
  await throwForStatus(res)
  return deserializeEmailConfig(await res.json())
}

/** Saves (creates or replaces) the instance-wide email configuration. */
export async function saveEmailConfig(config: EmailConfigSavePayload): Promise<void> {
  const res = await apiFetch('/api/instance/config/email', {
    method: 'PUT',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(serializeEmailConfig(config)),
  })
  await throwForStatus(res)
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
export async function sendTestEmail(recipientEmail: string): Promise<TestEmailResult> {
  const res = await apiFetch('/api/instance/config/email/test', {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ recipientEmail }),
  })
  await throwForStatus(res)
  return (await res.json()) as TestEmailResult
}