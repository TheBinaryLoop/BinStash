<template>
  <div class="space-y-4">
    <!-- Tenant ID -->
    <div>
      <BaseInput
        :model-value="modelValue.tenantId"
        @update:model-value="update('tenantId', String($event ?? ''))"
        label="Tenant ID"
        required
        type="text"
        placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
      />
      <p class="mt-1 text-xs text-ink-subtle">
        Found in <strong>Azure Portal → Microsoft Entra ID → Overview</strong> as the "Directory (tenant) ID".
        Use <code class="font-mono">common</code> to allow any Microsoft account.
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
          placeholder="xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx"
          autocomplete="off"
        />
        <p class="mt-1 text-xs text-ink-subtle">
          "Application (client) ID" from your App Registration.
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
        <p v-else class="mt-1 text-xs text-ink-subtle">
          Created under <strong>App Registration → Certificates & secrets</strong>.
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
        placeholder="https://your-instance.example.com/auth/entra/callback"
      />
      <p class="mt-1 text-xs text-ink-subtle">
        Add this URI to the <strong>Redirect URIs</strong> list in your App Registration.
        Leave blank to use the default <code class="font-mono">/auth/entra/callback</code>.
      </p>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { IconEye, IconEyeOff, IconLock } from '@tabler/icons-vue'
import { BaseInput } from '@/shared/components/ui'
import { MASKED_VALUE, type SSOEntraConfig } from '@/api/instance'

const props = defineProps<{
  modelValue: SSOEntraConfig
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: SSOEntraConfig): void
}>()

const showSecret = ref(false)

function isMasked(value: string): boolean {
  return value === MASKED_VALUE
}

function update<K extends keyof SSOEntraConfig>(field: K, value: SSOEntraConfig[K]) {
  emit('update:modelValue', { ...props.modelValue, [field]: value })
}
</script>
