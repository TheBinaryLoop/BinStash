import type { SearchResult } from './types'

 // TODO: make them dependant on the logged in user's permissions and the instance configuration (e.g. hide SSO settings if SSO is not configured)

export const staticSearchItems: SearchResult[] = [
  {
    id: 'settings-general',
    type: 'setting',
    title: 'General Settings',
    subtitle: 'Configure instance-wide behaviour',
    url: '/instance/settings',
    keywords: ['settings', 'general', 'instance']
  },
  {
    id: 'settings-email',
    type: 'setting',
    title: 'Email Configuration',
    subtitle: 'Set up outgoing email provider',
    url: '/instance/settings#email',
    keywords: ['smtp', 'brevo', 'mail', 'email', 'provider']
  },
  {
    id: 'settings-tenancy',
    type: 'setting',
    title: 'Tenancy Configuration',
    subtitle: 'Set up tenancy settings',
    url: '/instance/settings#tenancy',
    keywords: ['tenancy', 'multi-tenant', 'single-tenant', 'settings']
  },
  {
    id: 'settings-instance-sso',
    type: 'setting',
    title: 'Instance SSO Configuration',
    subtitle: 'Set up single sign-on for the instance',
    url: '/instance/settings#sso',
    keywords: ['sso', 'single sign-on', 'ldap', 'oauth', 'authentication']
  },
  {
    id: 'settings-chunkstores',
    type: 'setting',
    title: 'Chunkstores Configuration',
    subtitle: 'Configure chunkstore settings',
    url: '/instance/settings#chunkstores',
    keywords: ['chunkstores', 'storage', 'settings']
  },
  {
    id: 'settings-storageclasses',
    type: 'setting',
    title: 'Storage Classes Configuration',
    subtitle: 'Configure storage class settings',
    url: '/instance/settings#storageclasses',
    keywords: ['storage classes', 'storage', 'settings']
  },
  {
    id: 'settings-storageclassmappings',
    type: 'setting',
    title: 'Storage Class Default Mappings Configuration',
    subtitle: 'Configure storage class default mappings',
    url: '/instance/settings#mappings',
    keywords: ['storage class default mappings', 'storage', 'settings']
  },
  {
    id: 'dashboard',
    type: 'page',
    title: 'Dashboard',
    subtitle: 'Overview of instance metrics and activity',
    url: '/instance',
    keywords: ['dashboard', 'overview', 'metrics', 'activity']
  },
  {
    id: 'tenants',
    type: 'page',
    title: 'Tenants',
    subtitle: 'Manage tenants',
    url: '/instance/tenants',
    keywords: ['tenants', 'organizations', 'workspaces']
  },
  {
    id: 'users',
    type: 'page',
    title: 'Users',
    subtitle: 'Manage users',
    url: '/instance/users',
    keywords: ['accounts', 'members']
  }
]