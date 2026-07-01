<template>
  <div :class="['rounded-card border border-hairline bg-card', accent ? 'overflow-hidden' : '', padded && !$slots.header && !$slots.footer ? bodyPad : '']">
    <!-- Optional gradient accent strip -->
    <div v-if="accent" class="h-0.5 w-full bg-linear-to-r from-brand-from to-brand-to" />

    <div v-if="$slots.header" class="flex items-center justify-between gap-3 border-b border-hairline px-5 py-4">
      <div class="min-w-0">
        <slot name="header" />
      </div>
      <div v-if="$slots.actions" class="flex shrink-0 items-center gap-2">
        <slot name="actions" />
      </div>
    </div>

    <div v-if="$slots.default" :class="($slots.header || $slots.footer || accent) && padded ? bodyPad : ''">
      <slot />
    </div>

    <div v-if="$slots.footer" class="flex items-center justify-between gap-3 border-t border-hairline px-5 py-4">
      <slot name="footer" />
    </div>
  </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'

const props = withDefaults(defineProps<{
  padded?: boolean
  accent?: boolean
  density?: 'normal' | 'compact'
}>(), {
  padded: true,
  accent: false,
  density: 'normal',
})

const bodyPad = computed(() => (props.density === 'compact' ? 'p-4' : 'p-5'))
</script>
