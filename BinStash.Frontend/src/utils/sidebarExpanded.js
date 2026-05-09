import { ref } from 'vue'

export const SIDEBAR_EXPANDED_STORAGE_KEY = 'sidebar-expanded'

export function getSidebarExpanded() {
  if (typeof window === 'undefined') return false

  const storedSidebarExpanded = window.localStorage.getItem(SIDEBAR_EXPANDED_STORAGE_KEY)
  return storedSidebarExpanded === null ? false : storedSidebarExpanded === 'true'
}

export function setSidebarExpanded(expanded) {
  if (typeof window === 'undefined') return

  window.localStorage.setItem(SIDEBAR_EXPANDED_STORAGE_KEY, String(expanded))
  document.body.classList.toggle('sidebar-expanded', expanded)
  sidebarExpandedState.value = expanded
}

export function toggleSidebarExpanded() {
  const nextValue = !getSidebarExpanded()
  setSidebarExpanded(nextValue)
  return nextValue
}

export const sidebarExpandedState = ref(getSidebarExpanded())