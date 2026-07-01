<template>
  <div class="mb-6 flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
    <div class="min-w-0">
      <div v-if="$slots.eyebrow" class="mb-1">
        <slot name="eyebrow" />
      </div>
      <h1 class="text-xl font-semibold tracking-tight text-ink-strong sm:text-2xl">
        <slot name="title">{{ title }}</slot>
      </h1>
      <p v-if="description || $slots.description" class="mt-1 text-sm text-ink-muted">
        <slot name="description">{{ description }}</slot>
      </p>
    </div>
    <div v-if="$slots.actions" class="flex shrink-0 flex-wrap items-center gap-2">
      <slot name="actions" />
    </div>
  </div>
</template>

<script setup lang="ts">
defineProps<{
  title?: string
  description?: string
}>()
</script>
