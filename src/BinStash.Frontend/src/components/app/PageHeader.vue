<script setup lang="ts">
defineProps<{
  title: string
  description?: string
}>()
</script>

<template>
  <header class="flex flex-col gap-4 sm:flex-row sm:items-start sm:justify-between">
    <div class="min-w-0 space-y-1">
      <div class="flex items-center gap-3">
        <h1 class="truncate text-xl font-semibold tracking-tight">{{ title }}</h1>
        <slot name="badge" />
      </div>
      <p v-if="description" class="text-muted-foreground text-sm">{{ description }}</p>
      <slot name="meta" />
    </div>

    <div class="flex shrink-0 items-center gap-2">
      <slot name="actions" />
    </div>
  </header>
</template>
