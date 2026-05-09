<template>
  <div class="space-y-4">
    <div>
      <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
        API Key <span class="text-rose-500">*</span>
      </label>
      <div class="relative">
        <input
          :value="modelValue.apiKey"
          @input="update('apiKey', ($event.target as HTMLInputElement).value)"
          :type="showKey ? 'text' : 'password'"
          :placeholder="isMasked(modelValue.apiKey) ? '' : 'xkeysib-…'"
          class="form-input w-full text-sm font-mono pr-10"
          :class="errors?.apiKey ? 'border-rose-300 dark:border-rose-500/60 focus:border-rose-400 focus:ring-rose-200' : ''"
        />
        <button
          type="button"
          @click="showKey = !showKey"
          class="absolute inset-y-0 right-0 px-3 flex items-center text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
          tabindex="-1"
        >
          <IconEye v-if="!showKey" class="w-4 h-4" />
          <IconEyeOff v-else class="w-4 h-4" />
        </button>
      </div>
      <p v-if="errors?.apiKey" class="mt-1 text-xs text-rose-600 dark:text-rose-400">{{ errors.apiKey }}</p>
      <p v-if="isMasked(modelValue.apiKey)" class="mt-1 text-xs text-amber-600 dark:text-amber-400 flex items-center gap-1">
        <IconLock class="w-3.5 h-3.5 shrink-0" />
        A key is saved. Enter a new value to replace it, or leave as-is to keep the current key.
      </p>
      <p v-else class="mt-1 text-xs text-gray-400 dark:text-gray-500">
        Found in your Brevo account under SMTP & API → API Keys.
      </p>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { IconEye, IconEyeOff, IconLock } from '@tabler/icons-vue'
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