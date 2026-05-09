const RECENT_TENANT_SWITCHES_KEY = 'search-recent-tenant-switches'
const RECENT_TENANT_SWITCHES_LIMIT = 6

export function readRecentTenantSwitches(): string[] {
  try {
    const raw = localStorage.getItem(RECENT_TENANT_SWITCHES_KEY)
    const parsed = raw ? JSON.parse(raw) : []
    return Array.isArray(parsed)
      ? parsed.filter((id): id is string => typeof id === 'string' && id.length > 0)
      : []
  } catch {
    return []
  }
}

export function rememberRecentTenantSwitch(tenantId: string) {
  if (!tenantId) return

  const deduped = [
    tenantId,
    ...readRecentTenantSwitches().filter(id => id !== tenantId),
  ].slice(0, RECENT_TENANT_SWITCHES_LIMIT)

  localStorage.setItem(RECENT_TENANT_SWITCHES_KEY, JSON.stringify(deduped))
}
