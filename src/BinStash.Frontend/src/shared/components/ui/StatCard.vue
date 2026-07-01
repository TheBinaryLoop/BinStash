<template>
  <div class="rounded-card border border-hairline bg-card p-5">
    <div class="flex items-start justify-between gap-4">
      <div class="min-w-0">
        <p class="text-xs font-medium uppercase tracking-wide text-ink-subtle">{{ label }}</p>
        <p class="mt-2 text-2xl font-semibold tracking-tight text-ink-strong">
          <slot name="value">{{ value }}</slot>
        </p>
        <p v-if="hint || $slots.hint" class="mt-1 text-xs text-ink-muted">
          <slot name="hint">{{ hint }}</slot>
        </p>
      </div>
      <div
        v-if="icon"
        class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl"
        :class="[toneBg, toneText]"
      >
        <component :is="icon" class="h-5 w-5" />
      </div>
    </div>
    <div v-if="$slots.footer" class="mt-4 border-t border-hairline pt-3">
      <slot name="footer" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed, type Component } from 'vue'

const props = withDefaults(defineProps<{
  label: string
  value?: string | number
  hint?: string
  icon?: Component
  tone?: 'accent' | 'success' | 'warning' | 'danger' | 'neutral'
}>(), {
  tone: 'accent',
})

const toneBg = computed(() => ({
  accent: 'bg-accent-soft',
  success: 'bg-success-soft',
  warning: 'bg-warning-soft',
  danger: 'bg-danger-soft',
  neutral: 'bg-raised',
}[props.tone]))

const toneText = computed(() => ({
  accent: 'text-accent',
  success: 'text-success',
  warning: 'text-warning',
  danger: 'text-danger',
  neutral: 'text-ink-muted',
}[props.tone]))
</script>
