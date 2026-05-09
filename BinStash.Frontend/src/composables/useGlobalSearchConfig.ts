export type GlobalSearchAvailability = 'authenticated' | 'always' | 'disabled'

// Central switch for where global search should be available.
// Change this value later if search should also work on public pages.
const availability: GlobalSearchAvailability = 'authenticated'

export function isGlobalSearchEnabled(params: { isAuthenticated: boolean }): boolean {
  switch (availability) {
    case 'always':
      return true
    case 'disabled':
      return false
    case 'authenticated':
    default:
      return params.isAuthenticated
  }
}

export function useGlobalSearchConfig() {
  return {
    availability,
    isGlobalSearchEnabled,
  }
}