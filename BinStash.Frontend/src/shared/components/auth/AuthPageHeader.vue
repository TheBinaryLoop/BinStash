<template>
  <div :class="['mb-8', centered ? 'text-center' : '']">
    <h1 class="text-3xl text-gray-800 dark:text-gray-100 font-bold">
      {{ title }}
    </h1>

    <p v-if="description" class="mt-2 text-sm text-gray-500 dark:text-gray-400">
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