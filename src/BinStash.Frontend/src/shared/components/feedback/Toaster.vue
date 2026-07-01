<template>
  <Teleport to="body">
    <div class="pointer-events-none fixed inset-x-0 top-0 z-[60] flex flex-col items-center gap-2 p-4 sm:inset-x-auto sm:right-0 sm:items-end">
      <TransitionGroup
        enter-active-class="transition duration-200 ease-out"
        enter-from-class="opacity-0 translate-y-[-8px] sm:translate-x-2 sm:translate-y-0"
        enter-to-class="opacity-100 translate-x-0 translate-y-0"
        leave-active-class="transition duration-150 ease-in absolute"
        leave-from-class="opacity-100"
        leave-to-class="opacity-0 sm:translate-x-2"
      >
        <div
          v-for="t in store.toasts"
          :key="t.id"
          class="pointer-events-auto flex w-full max-w-sm items-start gap-3 rounded-card border border-hairline bg-panel p-3.5 shadow-lg"
          role="status"
        >
          <span class="mt-0.5 flex h-7 w-7 shrink-0 items-center justify-center rounded-full" :class="toneBg(t.tone)">
            <component :is="icon(t.tone)" class="h-4 w-4" :class="toneText(t.tone)" />
          </span>
          <div class="min-w-0 flex-1">
            <p v-if="t.title" class="text-sm font-semibold text-ink-strong">{{ t.title }}</p>
            <p class="text-sm" :class="t.title ? 'text-ink-muted' : 'text-ink-strong'">{{ t.message }}</p>
          </div>
          <button
            type="button"
            class="-mr-1 -mt-1 flex h-6 w-6 shrink-0 items-center justify-center rounded-control text-ink-subtle transition hover:bg-raised hover:text-ink-strong"
            aria-label="Dismiss"
            @click="store.dismiss(t.id)"
          >
            <IconX class="h-4 w-4" />
          </button>
        </div>
      </TransitionGroup>
    </div>
  </Teleport>
</template>

<script setup lang="ts">
import {
  IconCircleCheck,
  IconAlertTriangle,
  IconInfoCircle,
  IconAlertCircle,
  IconX,
} from '@tabler/icons-vue'
import { useToastStore, type ToastTone } from '@/stores/toast'

const store = useToastStore()

function icon(tone: ToastTone) {
  return {
    success: IconCircleCheck,
    error: IconAlertCircle,
    warning: IconAlertTriangle,
    info: IconInfoCircle,
  }[tone]
}

function toneBg(tone: ToastTone) {
  return {
    success: 'bg-success-soft',
    error: 'bg-danger-soft',
    warning: 'bg-warning-soft',
    info: 'bg-accent-soft',
  }[tone]
}

function toneText(tone: ToastTone) {
  return {
    success: 'text-success',
    error: 'text-danger',
    warning: 'text-warning',
    info: 'text-accent',
  }[tone]
}
</script>
