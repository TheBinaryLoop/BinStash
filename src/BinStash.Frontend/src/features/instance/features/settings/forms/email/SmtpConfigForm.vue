<template>
  <div class="space-y-4">
    <!-- Host + Port row -->
    <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
      <div class="sm:col-span-2">
        <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
          SMTP Host <span class="text-rose-500">*</span>
        </label>
        <input
          :value="modelValue.host"
          @input="update('host', ($event.target as HTMLInputElement).value)"
          type="text"
          placeholder="e.g. smtp.example.com"
          class="form-input w-full text-sm"
          :class="errors?.host ? 'border-rose-300 dark:border-rose-500/60 focus:border-rose-400 focus:ring-rose-200' : ''"
        />
        <p v-if="errors?.host" class="mt-1 text-xs text-rose-600 dark:text-rose-400">{{ errors.host }}</p>
      </div>
      <div>
        <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
          Port <span class="text-rose-500">*</span>
        </label>
        <input
          :value="modelValue.port"
          @input="update('port', Number(($event.target as HTMLInputElement).value))"
          type="number"
          min="1"
          max="65535"
          placeholder="587"
          class="form-input w-full text-sm"
          :class="errors?.port ? 'border-rose-300 dark:border-rose-500/60 focus:border-rose-400 focus:ring-rose-200' : ''"
        />
        <p v-if="errors?.port" class="mt-1 text-xs text-rose-600 dark:text-rose-400">{{ errors.port }}</p>
      </div>
    </div>

    <!-- Security mode -->
    <div>
      <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
        Security
      </label>
      <div class="flex flex-wrap gap-2">
        <button
          v-for="opt in SECURITY_OPTIONS"
          :key="opt.value"
          type="button"
          @click="update('security', opt.value)"
          class="px-3 py-1.5 rounded-lg text-xs font-medium border transition"
          :class="modelValue.security === opt.value
            ? 'bg-violet-500 border-violet-500 text-white'
            : 'bg-white dark:bg-gray-800 border-gray-200 dark:border-gray-600 text-gray-600 dark:text-gray-400 hover:border-gray-300 dark:hover:border-gray-500'"
        >
          {{ opt.label }}
        </button>
      </div>
      <p v-if="errors?.security" class="mt-1 text-xs text-rose-600 dark:text-rose-400">{{ errors.security }}</p>
      <p class="mt-1 text-xs text-gray-400 dark:text-gray-500">
        <span v-if="modelValue.security === 'none'">No encryption — only use on trusted internal networks.</span>
        <span v-else-if="modelValue.security === 'starttls'">Upgrades the connection to TLS after connecting (port 587 is typical).</span>
        <span v-else>Connects over TLS from the start (port 465 is typical).</span>
      </p>
    </div>

    <!-- Username + Password row -->
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
      <div>
        <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
          Username
        </label>
        <input
          :value="modelValue.username"
          @input="update('username', ($event.target as HTMLInputElement).value)"
          type="text"
          placeholder="e.g. user@example.com"
          autocomplete="off"
          class="form-input w-full text-sm"
          :class="errors?.username ? 'border-rose-300 dark:border-rose-500/60 focus:border-rose-400 focus:ring-rose-200' : ''"
        />
        <p v-if="errors?.username" class="mt-1 text-xs text-rose-600 dark:text-rose-400">{{ errors.username }}</p>
        <p class="mt-1 text-xs text-gray-400 dark:text-gray-500">Leave blank for unauthenticated relay.</p>
      </div>
      <div>
        <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
          Password
        </label>
        <div class="relative">
          <input
            :value="modelValue.password"
            @input="update('password', ($event.target as HTMLInputElement).value)"
            :type="showPassword ? 'text' : 'password'"
            placeholder="••••••••"
            autocomplete="new-password"
            class="form-input w-full text-sm pr-10"
            :class="errors?.password ? 'border-rose-300 dark:border-rose-500/60 focus:border-rose-400 focus:ring-rose-200' : ''"
          />
          <button
            type="button"
            @click="showPassword = !showPassword"
            class="absolute inset-y-0 right-0 px-3 flex items-center text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
            tabindex="-1"
          >
            <IconEye v-if="!showPassword" class="w-4 h-4" />
            <IconEyeOff v-else class="w-4 h-4" />
          </button>
        </div>
        <p v-if="errors?.password" class="mt-1 text-xs text-rose-600 dark:text-rose-400">{{ errors.password }}</p>
        <p v-if="isMasked(modelValue.password)" class="mt-1 text-xs text-amber-600 dark:text-amber-400 flex items-center gap-1">
          <IconLock class="w-3.5 h-3.5 shrink-0" />
          A password is saved. Enter a new value to replace it, or leave as-is.
        </p>
      </div>
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref } from 'vue'
import { IconEye, IconEyeOff, IconLock } from '@tabler/icons-vue'
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