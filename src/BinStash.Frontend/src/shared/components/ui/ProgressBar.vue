<template>
  <div>
    <div v-if="$slots.label || label" class="mb-1.5 flex items-center justify-between text-xs">
      <span class="font-medium text-ink-muted"><slot name="label">{{ label }}</slot></span>
      <span v-if="showValue" class="font-semibold text-ink-strong tabular-nums">{{ Math.round(clamped) }}%</span>
    </div>
    <div class="h-2 w-full overflow-hidden rounded-full bg-raised">
      <div
        class="h-full rounded-full transition-[width] duration-300 ease-out"
        :class="toneClass"
        :style="{ width: clamped + '%' }"
      />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  value: number
  max?: number
  tone?: 'accent' | 'success' | 'warning' | 'danger'
  label?: string
  showValue?: boolean
}>(), {
  max: 100,
  tone: 'accent',
  showValue: true,
})

const clamped = computed(() => Math.min(100, Math.max(0, (props.value / (props.max || 100)) * 100)))

const toneClass = computed(() => ({
  accent: 'bg-accent',
  success: 'bg-success',
  warning: 'bg-warning',
  danger: 'bg-danger',
}[props.tone]))
</script>
