import { defineStore } from 'pinia'
import { ref } from 'vue'

export type ToastTone = 'success' | 'error' | 'info' | 'warning'

export interface Toast {
  id: number
  tone: ToastTone
  title?: string
  message: string
  timeout: number
}

export interface ToastOptions {
  title?: string
  /** Auto-dismiss delay in ms. 0 disables auto-dismiss. */
  timeout?: number
}

export const useToastStore = defineStore('toast', () => {
  const toasts = ref<Toast[]>([])
  let seq = 0

  function push(tone: ToastTone, message: string, opts: ToastOptions = {}): number {
    const id = ++seq
    const timeout = opts.timeout ?? 4500
    toasts.value.push({ id, tone, message, title: opts.title, timeout })
    if (timeout > 0) {
      window.setTimeout(() => dismiss(id), timeout)
    }
    return id
  }

  function dismiss(id: number) {
    const i = toasts.value.findIndex((t) => t.id === id)
    if (i !== -1) toasts.value.splice(i, 1)
  }

  function clear() {
    toasts.value = []
  }

  return { toasts, push, dismiss, clear }
})
