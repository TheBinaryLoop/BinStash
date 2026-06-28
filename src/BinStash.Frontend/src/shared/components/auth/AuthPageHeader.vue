<template>
  <div :class="['mb-8', centered ? 'text-center' : '']">
    <h1 class="text-2xl font-bold tracking-tight text-ink-strong">
      {{ title }}
    </h1>

    <p v-if="description" class="mt-2 text-sm text-ink-muted">
      {{ description }}
    </p>

    <slot />
  </div>
</template>

<script setup lang="ts">
withDefaults(
  defineProps<{
    title: string
    description?: string
    centered?: boolean
  }>(),
  {
    description: undefined,
    centered: false,
  }
)
</script>