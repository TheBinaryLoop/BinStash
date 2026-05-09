<!-- 
import Spinner from '@/shared/components/feedback/Spinner.vue'

Default (32px violet)
<Spinner />

Larger, sky-colored
<Spinner :size="48" color="var(--color-sky-500)" />

Small, inline, thin
<Spinner :size="20" :thickness="2" />

Full-page centered loading state
<div class="flex items-center justify-center h-64">
  <Spinner :size="40" label="Fetching data…" />
</div>

Button with loading state
<button :disabled="loading">
  <Spinner v-if="loading" :size="16" :thickness="2" color="white" class="mr-2" />
  Save
</button> 
-->

<template>
  <svg
    class="animate-spin"
    :width="size"
    :height="size"
    viewBox="0 0 32 32"
    fill="none"
    xmlns="http://www.w3.org/2000/svg"
    role="status"
    :aria-label="label"
  >
    <!-- Track ring -->
    <circle
      cx="16"
      cy="16"
      :r="radius"
      :stroke-width="thickness"
      stroke="currentColor"
      class="text-gray-200 dark:text-gray-700/60"
    />
    <!-- Spinning arc -->
    <circle
      cx="16"
      cy="16"
      :r="radius"
      :stroke-width="thickness"
      :stroke="color"
      stroke-linecap="round"
      :stroke-dasharray="circumference"
      :stroke-dashoffset="dashOffset"
      transform="rotate(-90 16 16)"
    />
  </svg>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(
  defineProps<{
    /** Pixel size of the spinner (width and height). Default: 32 */
    size?: number | string
    /** Stroke thickness in px. Default: 3 */
    thickness?: number
    /**
     * Stroke color of the arc. Accepts any CSS color value or
     * a CSS custom property, e.g. "var(--color-sky-500)".
     * Default: "var(--color-violet-500)"
     */
    color?: string
    /** Accessible label for screen readers. Default: "Loading…" */
    label?: string
    /**
     * Fraction of the circle that is visible (0–1).
     * 0.75 means a ¾ arc. Default: 0.75
     */
    arc?: number
  }>(),
  {
    size: 32,
    thickness: 3,
    color: 'var(--color-violet-500)',
    label: 'Loading…',
    arc: 0.75,
  },
)

/** Radius that keeps the stroke inside the 32×32 viewBox for any thickness. */
const radius = computed(() => 16 - props.thickness / 2 - 1)
const circumference = computed(() => 2 * Math.PI * radius.value)
/** dashOffset hides (1 - arc) of the circle, leaving `arc` fraction visible. */
const dashOffset = computed(() => circumference.value * (1 - props.arc))
</script>