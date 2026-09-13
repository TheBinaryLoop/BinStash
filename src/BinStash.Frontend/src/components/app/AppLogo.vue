<script setup lang="ts">
withDefaults(defineProps<{ size?: number }>(), { size: 28 })
</script>

<template>
  <!-- Three offset blocks converging into one: many chunks, stored once. -->
  <svg
    :width="size"
    :height="size"
    viewBox="0 0 32 32"
    fill="none"
    aria-hidden="true"
    class="shrink-0"
  >
    <rect x="2" y="7" width="12" height="7" rx="1.5" class="fill-primary/25" />
    <rect x="2" y="17" width="12" height="7" rx="1.5" class="fill-primary/45" />
    <rect x="17" y="12" width="13" height="9" rx="2" class="fill-primary" />
    <path d="M14 10.5h2.5M14 20.5h2.5" class="stroke-primary/70" stroke-width="1.5" stroke-linecap="round" />
  </svg>
</template>
