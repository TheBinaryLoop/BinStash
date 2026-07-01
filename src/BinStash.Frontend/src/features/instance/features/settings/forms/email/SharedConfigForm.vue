<template>
  <div class="space-y-4">
    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
      <BaseInput
        :model-value="modelValue.fromEmail"
        @update:model-value="update('fromEmail', String($event ?? ''))"
        label="From Email"
        required
        type="email"
        placeholder="e.g. noreply@example.com"
        :error="errors?.fromEmail ?? undefined"
        hint="The address shown in the &quot;From&quot; field of all outgoing email."
      />
      <BaseInput
        :model-value="modelValue.supportEmail"
        @update:model-value="update('supportEmail', String($event ?? ''))"
        label="Support Email"
        required
        type="email"
        placeholder="e.g. support@example.com"
        :error="errors?.supportEmail ?? undefined"
        hint="Shown as the reply-to / support contact in email footers."
      />
    </div>
  </div>
</template>

<script lang="ts" setup>
import { BaseInput } from '@/shared/components/ui'
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
