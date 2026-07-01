<template>
  <div class="space-y-4">
    <!-- Client ID + Client Secret row -->
    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
      <div>
        <BaseInput
          :model-value="modelValue.clientId"
          @update:model-value="update('clientId', String($event ?? ''))"
          label="Client ID"
          required
          type="text"
          placeholder="xxxxxxxxxx-xxxxxxxxxxxxxxxxxxxxxxxxxxxxxxxx.apps.googleusercontent.com"
          autocomplete="off"
        />
        <p class="mt-1 text-xs text-ink-subtle">
          Found in <strong>Google Cloud Console → APIs & Services → Credentials</strong> under your OAuth 2.0 Client ID.
        </p>
      </div>
      <div>
        <BaseInput
          :model-value="modelValue.clientSecret"
          @update:model-value="update('clientSecret', String($event ?? ''))"
          label="Client Secret"
          required
          :type="showSecret ? 'text' : 'password'"
          placeholder="••••••••"
          autocomplete="new-password"
        >
          <template #suffix>
            <button
              type="button"
              @click="showSecret = !showSecret"
              class="flex items-center px-1 text-ink-subtle transition hover:text-ink-strong"
              tabindex="-1"
            >
              <IconEye v-if="!showSecret" class="h-4 w-4" />
              <IconEyeOff v-else class="h-4 w-4" />
            </button>
          </template>
        </BaseInput>
        <p v-if="isMasked(modelValue.clientSecret)" class="mt-1 flex items-center gap-1 text-xs text-warning">
          <IconLock class="h-3.5 w-3.5 shrink-0" />
          A secret is saved. Enter a new value to replace it, or leave as-is.
        </p>
      </div>
    </div>

    <!-- Redirect URI -->
    <div>
      <BaseInput
        :model-value="modelValue.redirectUri"
        @update:model-value="update('redirectUri', String($event ?? ''))"
        label="Redirect URI"
        type="text"
        placeholder="https://your-instance.example.com/auth/google/callback"
      />
      <p class="mt-1 text-xs text-ink-subtle">
        Add this URI to the <strong>Authorized redirect URIs</strong> list in your OAuth 2.0 Client.
        Leave blank to use the default <code class="font-mono">/auth/google/callback</code>.
      </p>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { IconEye, IconEyeOff, IconLock } from '@tabler/icons-vue'
import { BaseInput } from '@/shared/components/ui'
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
