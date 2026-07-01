<template>
  <div class="rounded-control border p-4" :class="toneContainer">
    <div class="flex items-center gap-1.5">
      <p class="text-xs font-medium uppercase tracking-wide" :class="toneLabel">{{ label }}</p>
      <BaseTooltip v-if="info" :content="info" />
    </div>
    <p class="mt-1.5 text-2xl font-semibold tracking-tight" :class="toneValue">
      <slot name="value">{{ value }}</slot>
    </p>
    <p v-if="description || $slots.default" class="mt-1 text-sm leading-5" :class="toneDesc">
      <slot>{{ description }}</slot>
    </p>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import BaseTooltip from './BaseTooltip.vue'

const props = withDefaults(defineProps<{
  label: string
  value?: string | number
  description?: string
  tone?: 'good' | 'bad' | 'neutral'
  info?: string
}>(), {
  tone: 'neutral',
})

const toneContainer = computed(() => ({
  good: 'border-success/25 bg-success-soft',
  bad: 'border-warning/25 bg-warning-soft',
  neutral: 'border-hairline bg-card',
}[props.tone]))

const toneLabel = computed(() => ({
  good: 'text-success',
  bad: 'text-warning',
  neutral: 'text-ink-subtle',
}[props.tone]))

const toneValue = computed(() => ({
  good: 'text-success',
  bad: 'text-warning',
  neutral: 'text-ink-strong',
}[props.tone]))

const toneDesc = computed(() => ({
  good: 'text-success/90',
  bad: 'text-warning/90',
  neutral: 'text-ink-muted',
}[props.tone]))
</script>
