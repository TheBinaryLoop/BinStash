import Fuse from 'fuse.js'
import { staticSearchItems } from './search-index'
import { useTenantStore } from '@/stores/tenant'
import type { SearchResult } from './types'
import { readRecentTenantSwitches } from './action-history'

const fuse = new Fuse(staticSearchItems, {
  threshold: 0.35,
  keys: ['title', 'subtitle', 'keywords']
})

const tenantKeywords = ['tenant', 'switch', 'workspace', 'organization', 'org']
const tenantAliasPrefixes = ['st ', 'switch ']

function parseTenantAliasQuery(query: string) {
  const trimmed = query.trim()
  const lowered = trimmed.toLowerCase()

  for (const prefix of tenantAliasPrefixes) {
    if (lowered.startsWith(prefix)) {
      return {
        isAlias: true,
        tenantQuery: trimmed.slice(prefix.length).trim(),
      }
    }
  }

  return {
    isAlias: false,
    tenantQuery: trimmed,
  }
}

function prioritizeRecentTenantActions(items: SearchResult[]): SearchResult[] {
  const recentOrder = readRecentTenantSwitches()
  if (recentOrder.length === 0) return items

  const orderByTenantId = new Map(recentOrder.map((tenantId, index) => [tenantId, index]))

  return [...items].sort((a, b) => {
    const aOrder = a.tenantId ? orderByTenantId.get(a.tenantId) : undefined
    const bOrder = b.tenantId ? orderByTenantId.get(b.tenantId) : undefined

    if (aOrder === undefined && bOrder === undefined) return a.title.localeCompare(b.title)
    if (aOrder === undefined) return 1
    if (bOrder === undefined) return -1
    return aOrder - bOrder
  })
}

function tenantToSwitchActionResult(tenant: { tenantId: string; name: string; slug?: string; role: string }): SearchResult {
  return {
    id: `action-switch-tenant-${tenant.tenantId}`,
    type: 'action',
    actionKind: 'switch-tenant',
    tenantId: tenant.tenantId,
    title: tenant.name,
    subtitle: tenant.slug
      ? `Switch tenant • ${tenant.slug} • ${tenant.role}`
      : `Switch tenant • ${tenant.role}`,
    keywords: [
      'switch tenant',
      'tenant',
      'workspace',
      'organization',
      tenant.name,
      tenant.slug ?? '',
      tenant.role,
    ],
  }
}

export function searchLocal(query: string) {
  if (!query.trim()) return []

  const aliasQuery = parseTenantAliasQuery(query)
  const normalizedQuery = query.trim().toLowerCase()
  const staticResults = aliasQuery.isAlias ? [] : fuse.search(query).map(x => x.item)

  const tenantStore = useTenantStore()
  const tenantActionItems = tenantStore.tenants.map(tenantToSwitchActionResult)

  console.log(tenantActionItems)

  if (tenantActionItems.length === 0) {
    return staticResults
  }

  const hasTenantIntent = aliasQuery.isAlias || tenantKeywords.some(keyword => normalizedQuery.includes(keyword))

  const tenantFuse = new Fuse(tenantActionItems, {
    threshold: 0.35,
    keys: ['title', 'subtitle', 'keywords'],
  })

  const isIntentOnlyKeyword = tenantKeywords.includes(normalizedQuery)
  const shouldShowAllTenantActions =
    (aliasQuery.isAlias && aliasQuery.tenantQuery.length === 0) ||
    (!aliasQuery.isAlias && isIntentOnlyKeyword)

  const tenantResults = hasTenantIntent
    ? shouldShowAllTenantActions
      ? tenantActionItems
      : tenantFuse.search(aliasQuery.isAlias ? aliasQuery.tenantQuery : query).map(x => x.item)
    : tenantFuse.search(query).map(x => x.item)

  const prioritizedTenantResults = prioritizeRecentTenantActions(tenantResults)

  return [...prioritizedTenantResults, ...staticResults]
}