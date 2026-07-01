<template>
  <FormField :label="label" :hint="hint" :error="error" :required="required" :for-id="id">
    <textarea
      :id="id"
      v-model="model"
      :rows="rows"
      :placeholder="placeholder"
      :disabled="disabled"
      :required="required"
      :class="[
        'w-full rounded-control border bg-card px-3 py-2 text-sm text-ink-strong placeholder:text-ink-subtle transition',
        'focus:outline-none focus:ring-2 focus:ring-accent/30 disabled:opacity-50 resize-y',
        error ? 'border-danger focus:border-danger' : 'border-hairline focus:border-accent',
      ]"
    />
  </FormField>
</template>

<script setup lang="ts">
import { useId } from 'vue'
import FormField from './FormField.vue'

const model = defineModel<string>()

withDefaults(defineProps<{
  label?: string
  hint?: string
  error?: string
  placeholder?: string
  disabled?: boolean
  required?: boolean
  rows?: number
}>(), {
  rows: 4,
})

const id = useId()
</script>
