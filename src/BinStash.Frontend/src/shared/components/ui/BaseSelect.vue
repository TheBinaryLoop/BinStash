<template>
  <FormField :label="label" :hint="hint" :error="error" :required="required" :for-id="id">
    <div class="relative">
      <select
        :id="id"
        v-model="model"
        :disabled="disabled"
        :required="required"
        :class="[
          'h-10 w-full appearance-none rounded-control border bg-card pl-3 pr-9 text-sm text-ink-strong transition',
          'focus:outline-none focus:ring-2 focus:ring-accent/30 disabled:opacity-50',
          error ? 'border-danger focus:border-danger' : 'border-hairline focus:border-accent',
        ]"
      >
        <option v-if="placeholder" value="" disabled>{{ placeholder }}</option>
        <slot>
          <option v-for="opt in normalizedOptions" :key="String(opt.value)" :value="opt.value">
            {{ opt.label }}
          </option>
        </slot>
      </select>
      <IconChevronDown class="pointer-events-none absolute right-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-subtle" />
    </div>
  </FormField>
</template>

<script setup lang="ts">
import { computed, useId } from 'vue'
import { IconChevronDown } from '@tabler/icons-vue'
import FormField from './FormField.vue'

const model = defineModel<string | number | null>()

const props = defineProps<{
  label?: string
  hint?: string
  error?: string
  placeholder?: string
  disabled?: boolean
  required?: boolean
  options?: Array<{ value: string | number; label: string } | string>
}>()

const normalizedOptions = computed(() =>
  (props.options ?? []).map((o) => (typeof o === 'string' ? { value: o, label: o } : o)),
)

const id = useId()
</script>
