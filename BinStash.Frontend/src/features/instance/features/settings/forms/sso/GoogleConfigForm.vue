<template>
  <div class="space-y-4">
    <!-- Client ID + Client Secret row -->
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
      <div>
        <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
          Client ID <span class="text-rose-500">*</span>
        </label>
        <input
          :value="modelValue.clientId"
          @input="update('clientId', ($event.target as HTMLInputElement).value)"
          type="text"
          placeholder="xxxxxxxxxx-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com"
          autocomplete="off"
          class="form-input w-full text-sm font-mono"
        />
        <p class="mt-1 text-xs text-gray-400 dark:text-gray-500">
          Found in <strong>Google Cloud Console → APIs & Services → Credentials</strong> under your OAuth 2.0 Client ID.
        </p>
      </div>
      <div>
        <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
          Client Secret <span class="text-rose-500">*</span>
        </label>
        <div class="relative">
          <input
            :value="modelValue.clientSecret"
            @input="update('clientSecret', ($event.target as HTMLInputElement).value)"
            :type="showSecret ? 'text' : 'password'"
            placeholder="••••••••"
            autocomplete="new-password"
            class="form-input w-full text-sm pr-10"
          />
          <button
            type="button"
            @click="showSecret = !showSecret"
            class="absolute inset-y-0 right-0 px-3 flex items-center text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
            tabindex="-1"
          >
            <IconEye v-if="!showSecret" class="w-4 h-4" />
            <IconEyeOff v-else class="w-4 h-4" />
          </button>
        </div>
        <p v-if="isMasked(modelValue.clientSecret)" class="mt-1 text-xs text-amber-600 dark:text-amber-400 flex items-center gap-1">
          <IconLock class="w-3.5 h-3.5 shrink-0" />
          A secret is saved. Enter a new value to replace it, or leave as-is.
        </p>
      </div>
    </div>

    <!-- Redirect URI -->
    <div>
      <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
        Redirect URI
      </label>
      <input
        :value="modelValue.redirectUri"
        @input="update('redirectUri', ($event.target as HTMLInputElement).value)"
        type="text"
        placeholder="https://your-instance.example.com/auth/google/callback"
        class="form-input w-full text-sm"
      />
      <p class="mt-1 text-xs text-gray-400 dark:text-gray-500">
        Add this URI to the <strong>Authorized redirect URIs</strong> list in your OAuth 2.0 Client.
        Leave blank to use the default <code class="font-mono">/auth/google/callback</code>.
      </p>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { IconEye, IconEyeOff, IconLock } from '@tabler/icons-vue'
import { MASKED_VALUE, type SSOGoogleConfig } from '@/api/instance'

const props = defineProps<{
  modelValue: SSOGoogleConfig
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: SSOGoogleConfig): void
}>()

const showSecret = ref(false)

function isMasked(value: string): boolean {
  return value === MASKED_VALUE
}

function update<K extends keyof SSOGoogleConfig>(field: K, value: SSOGoogleConfig[K]) {
  emit('update:modelValue', { ...props.modelValue, [field]: value })
}
</script>