<script setup lang="ts">
import type { HoverCardRootEmits, HoverCardRootProps } from "reka-ui"
import { HoverCardRoot, useForwardPropsEmits } from "reka-ui"

const props = withDefaults(defineProps<HoverCardRootProps>(), {
  // Long enough not to fire while the pointer is crossing the row on its way somewhere
  // else, short enough that deliberately resting on a cell feels answered rather than laggy.
  openDelay: 200,
  closeDelay: 120,
})
const emits = defineEmits<HoverCardRootEmits>()

const forwarded = useForwardPropsEmits(props, emits)
</script>

<template>
  <HoverCardRoot data-slot="hover-card" v-bind="forwarded">
    <slot />
  </HoverCardRoot>
</template>
