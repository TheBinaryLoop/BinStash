<template>
  <div
    class="animate-pulse rounded-md bg-raised"
    :class="rounded === 'full' ? 'rounded-full' : rounded === 'card' ? 'rounded-card' : ''"
    :style="style"
  />
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  width?: string
  height?: string
  rounded?: 'md' | 'full' | 'card'
}>(), {
  width: '100%',
  height: '1rem',
  rounded: 'md',
})

const style = computed(() => ({ width: props.width, height: props.height }))
</script>
