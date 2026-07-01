<template>
  <span :class="classes">
    <component :is="icon" v-if="icon" class="h-3.5 w-3.5 shrink-0" />
    <span v-if="dot" class="h-1.5 w-1.5 shrink-0 rounded-full bg-current opacity-80" />
    <slot>{{ label }}</slot>
  </span>
</template>

<script setup lang="ts">
import { computed, type Component } from 'vue'

const props = withDefaults(defineProps<{
  tone?: 'neutral' | 'accent' | 'success' | 'warning' | 'danger'
  variant?: 'soft' | 'solid' | 'outline'
  size?: 'sm' | 'md'
  label?: string
  icon?: Component
  dot?: boolean
}>(), {
  tone: 'neutral',
  variant: 'soft',
  size: 'sm',
  dot: false,
})

const soft: Record<string, string> = {
  neutral: 'bg-raised text-ink-muted',
  accent: 'bg-accent-soft text-accent',
  success: 'bg-success-soft text-success',
  warning: 'bg-warning-soft text-warning',
  danger: 'bg-danger-soft text-danger',
}
const solid: Record<string, string> = {
  neutral: 'bg-ink-muted text-canvas',
  accent: 'bg-accent text-white',
  success: 'bg-success text-white',
  warning: 'bg-warning text-white',
  danger: 'bg-danger text-white',
}
const outline: Record<string, string> = {
  neutral: 'border border-hairline text-ink-muted',
  accent: 'border border-accent/40 text-accent',
  success: 'border border-success/40 text-success',
  warning: 'border border-warning/40 text-warning',
  danger: 'border border-danger/40 text-danger',
}

const classes = computed(() => [
  'inline-flex items-center gap-1.5 rounded-full font-medium whitespace-nowrap',
  props.size === 'sm' ? 'px-2.5 py-0.5 text-xs' : 'px-3 py-1 text-sm',
  props.variant === 'solid' ? solid[props.tone] : props.variant === 'outline' ? outline[props.tone] : soft[props.tone],
])
</script>
