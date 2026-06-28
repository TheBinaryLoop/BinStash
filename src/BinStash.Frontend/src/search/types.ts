export interface SearchResult {
  id: string
  type: 'setting' | 'tenant' | 'user' | 'repository' | 'release' | 'action' | 'page'
  title: string
  subtitle?: string
  url?: string
  actionKind?: 'switch-tenant'
  tenantId?: string
  icon?: string
  keywords?: string[]
}