<template>
  <div :class="['rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs', padded ? 'p-6 sm:p-8' : '']">
    <slot />
  </div>
</template>

<script setup lang="ts">
withDefaults(
  defineProps<{
    padded?: boolean
  }>(),
  {
    padded: true,
  }
)
</script>