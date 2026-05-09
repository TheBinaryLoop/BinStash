import { onMounted, onUnmounted } from 'vue'

export interface GlobalKeybindHandlers {
  onOpenSearch?: () => void
  isEnabled?: () => boolean
}

export function useGlobalKeybinds(handlers: GlobalKeybindHandlers) {
  function isTypingTarget(target: EventTarget | null): boolean {
    const el = target as HTMLElement | null
    if (!el) return false

    const tagName = el.tagName?.toLowerCase()
    return (
      tagName === 'input' ||
      tagName === 'textarea' ||
      tagName === 'select' ||
      el.isContentEditable
    )
  }

  function onKeyDown(event: KeyboardEvent) {
    if (handlers.isEnabled && !handlers.isEnabled()) {
      return
    }

    const key = event.key.toLowerCase()
    const ctrlOrMeta = event.ctrlKey || event.metaKey

    if (isTypingTarget(event.target)) {
      return
    }

    if (ctrlOrMeta && key === 'k') {
      event.preventDefault()
      handlers.onOpenSearch?.()
      return
    }
  }

  onMounted(() => {
    window.addEventListener('keydown', onKeyDown)
  })

  onUnmounted(() => {
    window.removeEventListener('keydown', onKeyDown)
  })
}