<template>
  <div class="space-y-4">
    <!-- Issuer -->
    <div>
      <BaseInput
        :model-value="modelValue.issuer"
        @update:model-value="update('issuer', String($event ?? ''))"
        label="Issuer URL"
        required
        type="text"
        placeholder="https://auth.example.com"
      />
      <p class="mt-1 text-xs text-ink-subtle">
        The base URL of your OIDC provider. The discovery document is expected at
        <code class="font-mono">{issuer}/.well-known/openid-configuration</code>.
      </p>
    </div>

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
          Application / client ID from your identity provider.
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
        placeholder="https://your-instance.example.com/auth/oidc/callback"
      />
      <p class="mt-1 text-xs text-ink-subtle">
        Register this URI as an allowed callback in your identity provider.
        Leave blank to use the default <code class="font-mono">/auth/oidc/callback</code>.
      </p>
    </div>

    <!-- Scopes -->
    <div>
      <BaseInput
        :model-value="modelValue.scopes"
        @update:model-value="update('scopes', String($event ?? ''))"
        label="Scopes"
        type="text"
        placeholder="openid profile email"
      />
      <p class="mt-1 text-xs text-ink-subtle">
        Space-separated list of OAuth 2.0 scopes to request.
        <code class="font-mono">openid</code>, <code class="font-mono">profile</code>, and <code class="font-mono">email</code> are required for basic SSO.
      </p>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { IconEye, IconEyeOff, IconLock } from '@tabler/icons-vue'
import { BaseInput } from '@/shared/components/ui'
import { MASKED_VALUE, type SSOOIDCConfig } from '@/api/instance'

const props = defineProps<{
  modelValue: SSOOIDCConfig
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: SSOOIDCConfig): void
}>()

const showSecret = ref(false)

function isMasked(value: string): boolean {
  return value === MASKED_VALUE
}

function update<K extends keyof SSOOIDCConfig>(field: K, value: SSOOIDCConfig[K]) {
  emit('update:modelValue', { ...props.modelValue, [field]: value })
}
</script>
