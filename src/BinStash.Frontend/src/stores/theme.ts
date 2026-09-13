import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'

export type ThemePreference = 'dark' | 'light' | 'system'

/** Keep in sync with the pre-paint resolver in index.html. */
const STORAGE_KEY = 'binstash.theme'

export const useThemeStore = defineStore('theme', () => {
  const preference = ref<ThemePreference>(readStoredPreference())
  const systemPrefersLight = ref(window.matchMedia('(prefers-color-scheme: light)').matches)

  window
    .matchMedia('(prefers-color-scheme: light)')
    .addEventListener('change', (event) => (systemPrefersLight.value = event.matches))

  // Dark is the product default, so "system" only yields light on an explicit light preference.
  const isDark = computed(() =>
    preference.value === 'system' ? !systemPrefersLight.value : preference.value === 'dark',
  )

  watch(
    [isDark, preference],
    () => {
      document.documentElement.classList.toggle('dark', isDark.value)
      try {
        if (preference.value === 'system') localStorage.removeItem(STORAGE_KEY)
        else localStorage.setItem(STORAGE_KEY, preference.value)
      } catch {
        /* private mode — the preference simply won't persist */
      }
    },
    { immediate: true },
  )

  function setPreference(next: ThemePreference) {
    preference.value = next
  }

  function toggle() {
    preference.value = isDark.value ? 'light' : 'dark'
  }

  return { preference, isDark, setPreference, toggle }
})

function readStoredPreference(): ThemePreference {
  try {
    const stored = localStorage.getItem(STORAGE_KEY)
    if (stored === 'dark' || stored === 'light') return stored
  } catch {
    /* ignore */
  }
  return 'system'
}
