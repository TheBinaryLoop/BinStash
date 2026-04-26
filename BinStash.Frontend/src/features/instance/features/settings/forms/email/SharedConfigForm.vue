<template>
  <div class="space-y-4">
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
      <div>
        <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
          From Email <span class="text-rose-500">*</span>
        </label>
        <input
          :value="modelValue.fromEmail"
          @input="update('fromEmail', ($event.target as HTMLInputElement).value)"
          type="email"
          placeholder="e.g. noreply@example.com"
          class="form-input w-full text-sm"
          :class="errors?.fromEmail ? 'border-rose-300 dark:border-rose-500/60 focus:border-rose-400 focus:ring-rose-200' : ''"
        />
        <p v-if="errors?.fromEmail" class="mt-1 text-xs text-rose-600 dark:text-rose-400">{{ errors.fromEmail }}</p>
        <p class="mt-1 text-xs text-gray-400 dark:text-gray-500">The address shown in the "From" field of all outgoing email.</p>
      </div>
      <div>
        <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
          Support Email <span class="text-rose-500">*</span>
        </label>
        <input
          :value="modelValue.supportEmail"
          @input="update('supportEmail', ($event.target as HTMLInputElement).value)"
          type="email"
          placeholder="e.g. support@example.com"
          class="form-input w-full text-sm"
          :class="errors?.supportEmail ? 'border-rose-300 dark:border-rose-500/60 focus:border-rose-400 focus:ring-rose-200' : ''"
        />
        <p v-if="errors?.supportEmail" class="mt-1 text-xs text-rose-600 dark:text-rose-400">{{ errors.supportEmail }}</p>
        <p class="mt-1 text-xs text-gray-400 dark:text-gray-500">Shown as the reply-to / support contact in email footers.</p>
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import type { SharedEmailConfig } from '@/api/instance'

const props = defineProps<{
  modelValue: SharedEmailConfig
  errors?: {
    fromEmail?: string | null
    supportEmail?: string | null
  }
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: SharedEmailConfig): void
}>()

function update<K extends keyof SharedEmailConfig>(field: K, value: SharedEmailConfig[K]) {
  emit('update:modelValue', { ...props.modelValue, [field]: value })
}
</script>