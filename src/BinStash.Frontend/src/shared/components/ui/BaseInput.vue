<template>
  <FormField :label="label" :hint="hint" :error="error" :required="required" :for-id="id">
    <div class="relative">
      <component :is="prefixIcon" v-if="prefixIcon" class="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-ink-subtle" />
      <input
        :id="id"
        v-model="model"
        :type="type"
        :placeholder="placeholder"
        :disabled="disabled"
        :required="required"
        :autocomplete="autocomplete"
        :inputmode="inputmode"
        :class="[
          'h-10 w-full rounded-control border bg-card text-sm text-ink-strong placeholder:text-ink-subtle transition',
          'focus:outline-none focus:ring-2 focus:ring-accent/30 disabled:opacity-50',
          prefixIcon ? 'pl-9 pr-3' : 'px-3',
          error ? 'border-danger focus:border-danger' : 'border-hairline focus:border-accent',
        ]"
      />
      <div v-if="$slots.suffix" class="absolute right-2 top-1/2 -translate-y-1/2">
        <slot name="suffix" />
      </div>
    </div>
  </FormField>
</template>

<script setup lang="ts">
import { useId, type Component } from 'vue'
import FormField from './FormField.vue'

const model = defineModel<string | number>()

withDefaults(defineProps<{
  label?: string
  hint?: string
  error?: string
  type?: string
  placeholder?: string
  disabled?: boolean
  required?: boolean
  autocomplete?: string
  inputmode?: 'text' | 'numeric' | 'email' | 'search' | 'tel' | 'url'
  prefixIcon?: Component
}>(), {
  type: 'text',
})

const id = useId()
</script>
