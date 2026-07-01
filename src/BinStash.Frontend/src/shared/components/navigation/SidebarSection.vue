<template>
  <div>
    <h3 v-if="title" class="mb-2 px-2 text-[11px] font-semibold uppercase tracking-[0.18em] text-ink-subtle">
      <span class="hidden text-center lg:block lg:sidebar-expanded:hidden" aria-hidden="true">•••</span>
      <span class="lg:hidden lg:sidebar-expanded:block">{{ title }}</span>
    </h3>
    <ul class="space-y-0.5">
      <slot />
    </ul>
  </div>
</template>

<script setup lang="ts">
defineProps<{ title?: string }>()
</script>
