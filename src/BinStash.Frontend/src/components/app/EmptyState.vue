<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{
  icon?: Component
  title: string
  description?: string
}>()
</script>

<template>
  <div
    class="border-hairline flex flex-col items-center justify-center rounded-lg border border-dashed px-6 py-14 text-center"
  >
    <div
      v-if="icon"
      class="bg-muted text-muted-foreground mb-4 flex size-11 items-center justify-center rounded-lg"
    >
      <component :is="icon" class="size-5" />
    </div>
    <p class="text-sm font-medium">{{ title }}</p>
    <p v-if="description" class="text-muted-foreground mt-1 max-w-sm text-sm">
      {{ description }}
    </p>
    <div class="mt-5 empty:hidden"><slot /></div>
  </div>
</template>
