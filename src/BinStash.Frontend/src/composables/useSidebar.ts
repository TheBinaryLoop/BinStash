import { useStorage } from '@vueuse/core'
import { computed } from 'vue'

/**
 * Sidebar collapse state, shared across layouts and persisted so it survives navigation
 * and reloads. Module-level so the toggle in the sidebar and the layout that sizes the
 * rail read the same value.
 */
const collapsed = useStorage('binstash.sidebar-collapsed', false)

const EXPANDED_WIDTH = '15.5rem'
const COLLAPSED_WIDTH = '3.5rem'

export function useSidebar() {
  return {
    collapsed,
    toggle: () => (collapsed.value = !collapsed.value),
    /** Bind on the layout root so both the rail and the content offset track it. */
    widthStyle: computed(() => ({
      '--sidebar-width': collapsed.value ? COLLAPSED_WIDTH : EXPANDED_WIDTH,
    })),
  }
}
