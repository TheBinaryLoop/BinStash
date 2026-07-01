<template>
  <div class="space-y-4">
    <div>
      <BaseInput
        :model-value="modelValue.apiKey"
        @update:model-value="update('apiKey', String($event ?? ''))"
        label="API Key"
        required
        :type="showKey ? 'text' : 'password'"
        :placeholder="isMasked(modelValue.apiKey) ? '' : 'xkeysib-…'"
        :error="errors?.apiKey ?? undefined"
      >
        <template #suffix>
          <button
            type="button"
            @click="showKey = !showKey"
            class="flex items-center px-1 text-ink-subtle transition hover:text-ink-strong"
            tabindex="-1"
          >
            <IconEye v-if="!showKey" class="h-4 w-4" />
            <IconEyeOff v-else class="h-4 w-4" />
          </button>
        </template>
      </BaseInput>
      <p v-if="isMasked(modelValue.apiKey)" class="mt-1 flex items-center gap-1 text-xs text-warning">
        <IconLock class="h-3.5 w-3.5 shrink-0" />
        A key is saved. Enter a new value to replace it, or leave as-is to keep the current key.
      </p>
      <p v-else class="mt-1 text-xs text-ink-subtle">
        Found in your Brevo account under SMTP & API → API Keys.
      </p>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { IconEye, IconEyeOff, IconLock } from '@tabler/icons-vue'
import { BaseInput } from '@/shared/components/ui'
import { MASKED_VALUE, type BrevoEmailConfig } from '@/api/instance'

const props = defineProps<{
  modelValue: BrevoEmailConfig
  errors?: {
    apiKey?: string | null
  }
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: BrevoEmailConfig): void
}>()

const showKey = ref(false)

function isMasked(value: string): boolean {
  return value === MASKED_VALUE
}

function update<K extends keyof BrevoEmailConfig>(field: K, value: BrevoEmailConfig[K]) {
  emit('update:modelValue', { ...props.modelValue, [field]: value })
}
</script>
