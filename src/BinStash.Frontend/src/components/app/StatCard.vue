<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{
  label: string
  value: string
  hint?: string
  icon?: Component
  /** Renders the value in mono/tabular figures — use for anything measured. */
  numeric?: boolean
}>()
</script>

<template>
  <div class="bg-card hairline rounded-lg p-4">
    <div class="flex items-start justify-between gap-3">
      <p class="text-muted-foreground text-xs font-medium tracking-wide uppercase">{{ label }}</p>
      <component v-if="icon" :is="icon" class="text-muted-foreground size-4 shrink-0" />
    </div>
    <p
      class="mt-2 text-2xl leading-none font-semibold"
      :class="numeric ? 'font-mono tabular-nums' : ''"
    >
      {{ value }}
    </p>
    <p v-if="hint" class="text-muted-foreground mt-2 text-xs">{{ hint }}</p>
  </div>
</template>
