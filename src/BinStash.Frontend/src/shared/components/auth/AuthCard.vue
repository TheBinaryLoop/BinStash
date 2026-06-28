<template>
  <div :class="['rounded-card border border-hairline bg-panel shadow-xs', padded ? 'p-6 sm:p-8' : '']">
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