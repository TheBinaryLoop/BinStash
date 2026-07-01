<template>
  <div class="space-y-4">
    <!-- Host + Port row -->
    <div class="grid grid-cols-1 gap-4 sm:grid-cols-3">
      <div class="sm:col-span-2">
        <BaseInput
          :model-value="modelValue.host"
          @update:model-value="update('host', String($event ?? ''))"
          label="SMTP Host"
          required
          type="text"
          placeholder="e.g. smtp.example.com"
          :error="errors?.host ?? undefined"
        />
      </div>
      <BaseInput
        :model-value="modelValue.port"
        @update:model-value="update('port', Number($event))"
        label="Port"
        required
        type="number"
        placeholder="587"
        :error="errors?.port ?? undefined"
      />
    </div>

    <!-- Security mode -->
    <FormField label="Security" :error="errors?.security ?? undefined">
      <BaseRadioGroup
        :model-value="modelValue.security"
        @update:model-value="update('security', $event as SmtpSecurityMode)"
        :options="SECURITY_OPTIONS"
        :columns="3"
      />
    </FormField>
    <p class="-mt-2 text-xs text-ink-subtle">
      <span v-if="modelValue.security === 'none'">No encryption — only use on trusted internal networks.</span>
      <span v-else-if="modelValue.security === 'starttls'">Upgrades the connection to TLS after connecting (port 587 is typical).</span>
      <span v-else>Connects over TLS from the start (port 465 is typical).</span>
    </p>

    <!-- Username + Password row -->
    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
      <BaseInput
        :model-value="modelValue.username"
        @update:model-value="update('username', String($event ?? ''))"
        label="Username"
        type="text"
        placeholder="e.g. user@example.com"
        autocomplete="off"
        :error="errors?.username ?? undefined"
        hint="Leave blank for unauthenticated relay."
      />
      <div>
        <BaseInput
          :model-value="modelValue.password"
          @update:model-value="update('password', String($event ?? ''))"
          label="Password"
          :type="showPassword ? 'text' : 'password'"
          placeholder="••••••••"
          autocomplete="new-password"
          :error="errors?.password ?? undefined"
        >
          <template #suffix>
            <button
              type="button"
              @click="showPassword = !showPassword"
              class="flex items-center px-1 text-ink-subtle transition hover:text-ink-strong"
              tabindex="-1"
            >
              <IconEye v-if="!showPassword" class="h-4 w-4" />
              <IconEyeOff v-else class="h-4 w-4" />
            </button>
          </template>
        </BaseInput>
        <p v-if="isMasked(modelValue.password)" class="mt-1 flex items-center gap-1 text-xs text-warning">
          <IconLock class="h-3.5 w-3.5 shrink-0" />
          A password is saved. Enter a new value to replace it, or leave as-is.
        </p>
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { IconEye, IconEyeOff, IconLock } from '@tabler/icons-vue'
import { BaseInput, BaseRadioGroup, FormField } from '@/shared/components/ui'
import { MASKED_VALUE, type SmtpEmailConfig, type SmtpSecurityMode } from '@/api/instance'

const props = defineProps<{
  modelValue: SmtpEmailConfig
  errors?: {
    host?: string | null
    port?: string | null
    username?: string | null
    password?: string | null
    security?: string | null
  }
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: SmtpEmailConfig): void
}>()

const showPassword = ref(false)

type SecurityOption = { value: SmtpSecurityMode; label: string }
const SECURITY_OPTIONS: SecurityOption[] = [
  { value: 'none', label: 'None' },
  { value: 'starttls', label: 'STARTTLS' },
  { value: 'ssl', label: 'SSL / TLS' },
]

function isMasked(value: string): boolean {
  return value === MASKED_VALUE
}

function update<K extends keyof SmtpEmailConfig>(field: K, value: SmtpEmailConfig[K]) {
  emit('update:modelValue', { ...props.modelValue, [field]: value })
}
</script>
