<script setup lang="ts">
import { Check, Copy } from '@lucide/vue'
import { useClipboard } from '@vueuse/core'

import { Button } from '@/components/ui/button'

const props = withDefaults(
  defineProps<{ value: string; label?: string; variant?: 'ghost' | 'outline' }>(),
  { variant: 'ghost' },
)

const { copy, copied, isSupported } = useClipboard({ copiedDuring: 1500 })
</script>

<template>
  <Button
    v-if="isSupported"
    type="button"
    :variant="props.variant"
    size="sm"
    class="gap-1.5"
    :aria-label="label ?? 'Copy to clipboard'"
    @click="copy(props.value)"
  >
    <Check v-if="copied" class="text-success size-3.5" />
    <Copy v-else class="size-3.5" />
    <span v-if="label" class="text-xs">{{ copied ? 'Copied' : label }}</span>
  </Button>
</template>
