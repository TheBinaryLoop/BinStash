<template>
  <span
    class="relative inline-flex"
    @mouseenter="show = true"
    @mouseleave="show = false"
    @focusin="show = true"
    @focusout="show = false"
  >
    <slot name="trigger">
      <IconHelpCircle class="h-4 w-4 text-ink-subtle" tabindex="0" />
    </slot>
    <Transition
      enter-active-class="transition duration-100 ease-out"
      enter-from-class="opacity-0 translate-y-1"
      enter-to-class="opacity-100 translate-y-0"
      leave-active-class="transition duration-75 ease-in"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <span
        v-if="show"
        role="tooltip"
        :class="[
          'absolute z-50 w-max max-w-xs rounded-lg border border-hairline bg-panel px-3 py-2 text-xs leading-relaxed text-ink-muted shadow-lg',
          placement === 'top' ? 'bottom-full left-1/2 mb-2 -translate-x-1/2' : 'top-full left-1/2 mt-2 -translate-x-1/2',
        ]"
      >
        <slot>{{ content }}</slot>
      </span>
    </Transition>
  </span>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { IconHelpCircle } from '@tabler/icons-vue'

withDefaults(defineProps<{
  content?: string
  placement?: 'top' | 'bottom'
}>(), {
  placement: 'top',
})

const show = ref(false)
</script>
