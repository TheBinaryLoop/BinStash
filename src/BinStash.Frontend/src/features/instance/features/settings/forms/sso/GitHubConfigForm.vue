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
          autocomplete="off"
        />
        <p class="mt-1 text-xs text-ink-subtle">
          Found in <strong>GitHub → Settings → Developer settings → OAuth Apps</strong>.
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
        label="Authorization Callback URL"
        type="text"
        placeholder="https://your-instance.example.com/auth/github/callback"
      />
      <p class="mt-1 text-xs text-ink-subtle">
        Must match the <strong>Authorization callback URL</strong> set in your GitHub OAuth App.
        Leave blank to use the default <code class="font-mono">/auth/github/callback</code>.
      </p>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { IconEye, IconEyeOff, IconLock } from '@tabler/icons-vue'
import { BaseInput } from '@/shared/components/ui'
import { MASKED_VALUE, type SSOGitHubConfig } from '@/api/instance'

const props = defineProps<{
  modelValue: SSOGitHubConfig
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: SSOGitHubConfig): void
}>()

const showSecret = ref(false)

function isMasked(value: string): boolean {
  return value === MASKED_VALUE
}

function update<K extends keyof SSOGitHubConfig>(field: K, value: SSOGitHubConfig[K]) {
  emit('update:modelValue', { ...props.modelValue, [field]: value })
}
</script>
