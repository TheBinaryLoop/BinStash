import { useToastStore, type ToastOptions } from '@/stores/toast'

/**
 * Convenience wrapper around the toast store.
 *
 *   const toast = useToast()
 *   toast.success('Repository created')
 *   toast.error('Could not save changes', { title: 'Save failed' })
 */
export function useToast() {
  const store = useToastStore()
  return {
    success: (message: string, opts?: ToastOptions) => store.push('success', message, opts),
    error: (message: string, opts?: ToastOptions) => store.push('error', message, opts),
    info: (message: string, opts?: ToastOptions) => store.push('info', message, opts),
    warning: (message: string, opts?: ToastOptions) => store.push('warning', message, opts),
    dismiss: store.dismiss,
    clear: store.clear,
  }
}
