<script setup lang="ts">
import type { HoverCardContentProps } from "reka-ui"
import type { HTMLAttributes } from "vue"
import { reactiveOmit } from "@vueuse/core"
import { HoverCardContent, HoverCardPortal, useForwardProps } from "reka-ui"
import { cn } from "@/lib/utils"

defineOptions({
  inheritAttrs: false,
})

const props = withDefaults(
  defineProps<HoverCardContentProps & { class?: HTMLAttributes["class"] }>(),
  {
    align: "start",
    sideOffset: 6,
    // Keep the card off the viewport edges so the available-height variable it exposes leaves
    // room to breathe rather than running the content flush to the bottom of the window.
    collisionPadding: 16,
  },
)

const delegatedProps = reactiveOmit(props, "class")

const forwarded = useForwardProps(delegatedProps)
</script>

<template>
  <HoverCardPortal>
    <HoverCardContent
      data-slot="hover-card-content"
      v-bind="{ ...$attrs, ...forwarded }"
      :class="
        cn(
          'bg-popover text-popover-foreground data-[state=open]:animate-in data-[state=closed]:animate-out data-[state=closed]:fade-out-0 data-[state=open]:fade-in-0 data-[state=closed]:zoom-out-95 data-[state=open]:zoom-in-95 data-[side=bottom]:slide-in-from-top-2 data-[side=left]:slide-in-from-right-2 data-[side=right]:slide-in-from-left-2 data-[side=top]:slide-in-from-bottom-2 z-50 w-80 max-w-(--reka-hover-card-content-available-width) rounded-md border p-0 shadow-md origin-(--reka-hover-card-content-transform-origin) outline-hidden',
          props.class,
        )
      "
    >
      <slot />
    </HoverCardContent>
  </HoverCardPortal>
</template>
