<template>
  <div class="space-y-6">
    <!-- Section header -->
    <div>
      <h2 class="text-lg font-semibold text-gray-800 dark:text-gray-100">Email Configuration</h2>
      <p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
        Configure the outgoing email provider used for notifications, invitations, and password resets.
      </p>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center gap-3 text-gray-500 dark:text-gray-400 py-8 justify-center">
      <Spinner />
      <span>Loading email configuration…</span>
    </div>

    <!-- Load error -->
    <div
      v-else-if="loadError"
      class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-4 text-sm text-rose-700 dark:text-rose-400"
    >
      {{ loadError }}
    </div>

    <template v-else>
      <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-6 border border-gray-100 dark:border-gray-700/60 space-y-6">

        <!-- Provider selector -->
        <div>
          <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
            Email Provider
          </label>
          <div class="flex flex-wrap gap-2">
            <button
              type="button"
              @click="config.provider = null"
              class="px-4 py-2 rounded-lg text-sm font-medium border transition"
              :class="config.provider === null
                ? 'bg-violet-500 border-violet-500 text-white'
                : 'bg-white dark:bg-gray-800 border-gray-200 dark:border-gray-600 text-gray-600 dark:text-gray-400 hover:border-gray-300 dark:hover:border-gray-500'"
            >
              None
            </button>
            <button
              v-for="p in EMAIL_PROVIDERS"
              :key="p.id"
              type="button"
              @click="config.provider = p.id"
              class="px-4 py-2 rounded-lg text-sm font-medium border transition"
              :class="config.provider === p.id
                ? 'bg-violet-500 border-violet-500 text-white'
                : 'bg-white dark:bg-gray-800 border-gray-200 dark:border-gray-600 text-gray-600 dark:text-gray-400 hover:border-gray-300 dark:hover:border-gray-500'"
            >
              {{ p.label }}
            </button>
          </div>
          <p class="mt-1.5 text-xs text-gray-400 dark:text-gray-500">
            Select <strong>None</strong> to disable outgoing email entirely.
          </p>
        </div>

        <template v-if="config.provider !== null">
          <!-- Shared settings -->
          <div class="pt-2 border-t border-gray-100 dark:border-gray-700/60 space-y-2">
            <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-400 dark:text-gray-500">Sender</h3>
            <SharedConfigForm v-model="config.shared" :errors="sharedFieldErrors" />
          </div>

          <!-- Provider-specific config -->
          <div class="pt-2 border-t border-gray-100 dark:border-gray-700/60 space-y-2">
            <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-400 dark:text-gray-500">
              {{ activeProviderLabel }} Settings
            </h3>
            <BrevoConfigForm v-if="config.provider === 'brevo'" v-model="config.brevo" :errors="brevoFieldErrors" />
            <SmtpConfigForm v-else-if="config.provider === 'smtp'" v-model="config.smtp" :errors="smtpFieldErrors" />
          </div>
        </template>

        <!-- No-provider notice -->
        <div
          v-else
          class="pt-2 border-t border-gray-100 dark:border-gray-700/60"
        >
          <p class="text-sm text-gray-500 dark:text-gray-400 italic">
            Outgoing email is disabled. Users will not receive email notifications or invitations.
          </p>
        </div>

        <!-- Save error -->
        <div
          v-if="saveError"
          class="bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 rounded-xl p-3 text-sm text-rose-700 dark:text-rose-400"
        >
          {{ saveError }}
        </div>

        <!-- Actions -->
        <div class="flex items-center gap-3 pt-2 border-t border-gray-100 dark:border-gray-700/60">
          <button
            type="button"
            @click="save"
            :disabled="saving"
            class="btn bg-violet-500 hover:bg-violet-600 text-white px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60 flex items-center gap-2"
          >
            <Spinner v-if="saving" class="w-4 h-4" />
            {{ saving ? 'Saving…' : 'Save' }}
          </button>
          <button
            type="button"
            @click="reset"
            :disabled="saving"
            class="btn bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700 text-gray-600 dark:text-gray-400 hover:border-gray-300 dark:hover:border-gray-600 px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60"
          >
            Reset
          </button>
        </div>

        <!-- ── Test email ───────────────────────────────────────────────── -->
        <div
          v-if="serverSnapshot.provider !== null"
          class="pt-4 border-t border-gray-100 dark:border-gray-700/60 space-y-3"
        >
          <div>
            <h3 class="text-sm font-medium text-gray-700 dark:text-gray-300">Send Test Email</h3>
            <p class="text-xs text-gray-400 dark:text-gray-500 mt-0.5">
              Verify the saved configuration can reach the provider by sending a test message.
            </p>
          </div>
          <div class="flex items-start gap-2">
            <input
              v-model="testRecipient"
              type="email"
              placeholder="recipient@example.com"
              class="form-input text-sm flex-1"
              :disabled="testing"
            />
            <button
              type="button"
              @click="sendTest"
              :disabled="testing || !testRecipient.trim()"
              class="btn bg-white dark:bg-gray-700 border border-gray-200 dark:border-gray-600 text-gray-700 dark:text-gray-200 hover:border-violet-300 dark:hover:border-violet-500 px-4 py-2 rounded-lg text-sm font-medium disabled:opacity-60 flex items-center gap-2 shrink-0"
            >
              <Spinner v-if="testing" class="w-4 h-4" />
              <IconSend v-else class="w-4 h-4" />
              {{ testing ? 'Sending…' : 'Send Test' }}
            </button>
          </div>
          <!-- Test result -->
          <div
            v-if="testResult"
            class="rounded-lg px-4 py-3 text-sm flex items-start gap-2"
            :class="testResult.success
              ? 'bg-green-50 dark:bg-green-500/10 border border-green-200 dark:border-green-500/30 text-green-700 dark:text-green-400'
              : 'bg-rose-50 dark:bg-rose-500/10 border border-rose-200 dark:border-rose-500/30 text-rose-700 dark:text-rose-400'"
          >
            <IconCircleCheck v-if="testResult.success" class="w-4 h-4 shrink-0 mt-0.5" />
            <IconAlertCircle v-else class="w-4 h-4 shrink-0 mt-0.5" />
            <div>
              <span v-if="testResult.success">
                Test email sent successfully to <strong>{{ testRecipient }}</strong>.
              </span>
              <template v-else>
                <span>Failed to send test email.</span>
                <div
                  v-if="testResult.providerError"
                  class="mt-1 font-mono text-xs break-all opacity-80"
                >
                  {{ testResult.providerError }}
                </div>
              </template>
            </div>
          </div>
        </div>

      </div>
    </template>

    <!-- Success toast -->
    <div
      v-if="successMsg"
      class="fixed bottom-4 right-4 z-50 bg-green-500 text-white text-sm font-medium px-4 py-2.5 rounded-xl shadow-lg flex items-center gap-2"
    >
      <IconCircleCheck class="w-4 h-4 shrink-0" />
      {{ successMsg }}
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref, reactive, computed, onMounted } from 'vue'
import { IconCircleCheck, IconAlertCircle, IconSend } from '@tabler/icons-vue'
import {
  fetchEmailConfig,
  saveEmailConfig,
  sendTestEmail,
  defaultEmailConfig,
  type EmailProvider,
  type EmailConfig,
  type EmailConfigSavePayload,
  type TestEmailResult,
} from '@/api/instance'
import { findFieldError, parseApiValidationError, type FieldErrorMap } from '@/utils/apiValidation'
import SharedConfigForm from '@/features/instance/features/settings/forms/email/SharedConfigForm.vue'
import BrevoConfigForm from '@/features/instance/features/settings/forms/email/BrevoConfigForm.vue'
import SmtpConfigForm from '@/features/instance/features/settings/forms/email/SmtpConfigForm.vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'

// ── Provider registry ─────────────────────────────────────────────────────────
// To add a new provider:
//   1. Add its config type to instance.ts and extend EmailConfig.
//   2. Create XxxConfigForm.vue in ./email/.
//   3. Add an entry here and a v-if branch in the template above.

const EMAIL_PROVIDERS: { id: EmailProvider; label: string }[] = [
  { id: 'brevo', label: 'Brevo' },
  { id: 'smtp', label: 'SMTP' },
]

// ── State ─────────────────────────────────────────────────────────────────────

const loading = ref(true)
const loadError = ref<string | null>(null)
const saving = ref(false)
const saveError = ref<string | null>(null)
const successMsg = ref<string | null>(null)
const fieldErrors = ref<FieldErrorMap>({})

const testing = ref(false)
const testRecipient = ref('')
const testResult = ref<TestEmailResult | null>(null)

/** Live form state — bound directly to sub-forms via v-model. */
const config = reactive<EmailConfig>(defaultEmailConfig())

/** Snapshot of the last server-saved state, used for Reset. */
let serverSnapshot: EmailConfig = defaultEmailConfig()

// ── Derived ───────────────────────────────────────────────────────────────────

const activeProviderLabel = computed(
  () => EMAIL_PROVIDERS.find(p => p.id === config.provider)?.label ?? '',
)

const sharedFieldErrors = computed(() => ({
  fromEmail: findFieldError(fieldErrors.value, 'shared.fromEmail', 'fromEmail'),
  supportEmail: findFieldError(fieldErrors.value, 'shared.supportEmail', 'supportEmail'),
}))

const brevoFieldErrors = computed(() => ({
  apiKey: findFieldError(fieldErrors.value, 'brevo.apiKey', 'apiKey'),
}))

const smtpFieldErrors = computed(() => ({
  host: findFieldError(fieldErrors.value, 'smtp.host', 'host'),
  port: findFieldError(fieldErrors.value, 'smtp.port', 'port'),
  username: findFieldError(fieldErrors.value, 'smtp.username', 'username'),
  password: findFieldError(fieldErrors.value, 'smtp.password', 'password'),
  security: findFieldError(fieldErrors.value, 'smtp.security', 'security'),
}))

// ── Methods ───────────────────────────────────────────────────────────────────

function applySnapshot(snap: EmailConfig) {
  config.provider = snap.provider
  config.shared = { ...snap.shared }
  config.brevo = { ...snap.brevo }
  config.smtp = { ...snap.smtp }
}

function reset() {
  applySnapshot(serverSnapshot)
  saveError.value = null
  fieldErrors.value = {}
}

async function load() {
  loading.value = true
  loadError.value = null
  try {
    const data = await fetchEmailConfig()
    serverSnapshot = {
      provider: data.provider,
      shared: { ...data.shared },
      brevo: { ...data.brevo },
      smtp: { ...data.smtp },
    }
    applySnapshot(serverSnapshot)
  } catch (e: any) {
    loadError.value = e.message || 'Failed to load email configuration.'
  } finally {
    loading.value = false
  }
}

async function save() {
  saveError.value = null
  fieldErrors.value = {}

  // Validate shared fields when a provider is selected
  if (config.provider !== null) {
    if (!config.shared.fromEmail.trim()) {
      fieldErrors.value = { 'shared.fromemail': 'From Email is required.' }
      saveError.value = 'Please correct the highlighted fields.'
      return
    }
    if (!config.shared.supportEmail.trim()) {
      fieldErrors.value = { 'shared.supportemail': 'Support Email is required.' }
      saveError.value = 'Please correct the highlighted fields.'
      return
    }
  }

  // Provider-specific validation
  if (config.provider === 'brevo') {
    if (!config.brevo.apiKey.trim()) {
      fieldErrors.value = { 'brevo.apikey': 'API Key is required.' }
      saveError.value = 'Please correct the highlighted fields.'
      return
    }
  } else if (config.provider === 'smtp') {
    if (!config.smtp.host.trim()) {
      fieldErrors.value = { 'smtp.host': 'SMTP Host is required.' }
      saveError.value = 'Please correct the highlighted fields.'
      return
    }
    if (!config.smtp.port || config.smtp.port < 1 || config.smtp.port > 65535) {
      fieldErrors.value = { 'smtp.port': 'A valid port (1-65535) is required.' }
      saveError.value = 'Please correct the highlighted fields.'
      return
    }
  }

  const payload: EmailConfigSavePayload = {
    provider: config.provider,
    shared: { ...config.shared },
  }
  if (config.provider === 'brevo') payload.brevo = { ...config.brevo }
  if (config.provider === 'smtp') payload.smtp = { ...config.smtp }

  saving.value = true
  try {
    await saveEmailConfig(payload)
    serverSnapshot = {
      provider: config.provider,
      shared: { ...config.shared },
      brevo: { ...config.brevo },
      smtp: { ...config.smtp },
    }
    showSuccess('Email configuration saved.')
  } catch (e: unknown) {
    const parsed = parseApiValidationError(e, 'Failed to save email configuration.')
    saveError.value = parsed.generalError
    fieldErrors.value = parsed.fieldErrors
  } finally {
    saving.value = false
  }
}

function showSuccess(msg: string) {
  successMsg.value = msg
  setTimeout(() => (successMsg.value = null), 3000)
}

async function sendTest() {
  if (!testRecipient.value.trim()) return
  testResult.value = null
  testing.value = true
  try {
    testResult.value = await sendTestEmail(testRecipient.value.trim())
  } catch (e: any) {
    testResult.value = { success: false, providerError: e.message || 'Unexpected error.' }
  } finally {
    testing.value = false
  }
}

onMounted(load)
</script>