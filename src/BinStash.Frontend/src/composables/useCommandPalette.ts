import { ref } from 'vue'

/**
 * Module-level so the header button and the ⌘K handler drive the same dialog,
 * without threading state through the layout.
 */
const open = ref(false)

export function useCommandPalette() {
  return {
    open,
    show: () => (open.value = true),
    toggle: () => (open.value = !open.value),
  }
}
