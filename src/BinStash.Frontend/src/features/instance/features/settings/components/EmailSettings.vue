<template>
  <div class="space-y-6">
    <!-- Section header -->
    <div>
      <h2 class="text-lg font-semibold text-ink-strong">Email Configuration</h2>
      <p class="mt-0.5 text-sm text-ink-muted">
        Configure the outgoing email provider used for notifications, invitations, and password resets.
      </p>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center justify-center gap-3 py-8 text-ink-muted">
      <Spinner :size="20" color="var(--color-accent)" />
      <span>Loading email configuration…</span>
    </div>

    <!-- Load error -->
    <div
      v-else-if="loadError"
      class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
    >
      {{ loadError }}
    </div>

    <template v-else>
      <BaseCard>
        <div class="space-y-6">

          <!-- Provider selector -->
          <div>
            <label class="mb-1.5 block text-sm font-medium text-ink-strong">
              Email Provider
            </label>
            <div class="flex flex-wrap gap-2">
              <button
                type="button"
                @click="config.provider = null"
                class="h-9 rounded-control border px-4 text-sm font-medium transition"
                :class="config.provider === null
                  ? 'border-accent bg-accent text-white'
                  : 'border-hairline bg-card text-ink-muted hover:bg-raised hover:text-ink-strong'"
              >
                None
              </button>
              <button
                v-for="p in EMAIL_PROVIDERS"
                :key="p.id"
                type="button"
                @click="config.provider = p.id"
                class="h-9 rounded-control border px-4 text-sm font-medium transition"
                :class="config.provider === p.id
                  ? 'border-accent bg-accent text-white'
                  : 'border-hairline bg-card text-ink-muted hover:bg-raised hover:text-ink-strong'"
              >
                {{ p.label }}
              </button>
            </div>
            <p class="mt-1.5 text-xs text-ink-subtle">
              Select <strong>None</strong> to disable outgoing email entirely.
            </p>
          </div>

          <template v-if="config.provider !== null">
            <!-- Shared settings -->
            <div class="space-y-2 border-t border-hairline pt-4">
              <h3 class="text-xs font-semibold uppercase tracking-wide text-ink-subtle">Sender</h3>
              <SharedConfigForm v-model="config.shared" :errors="sharedFieldErrors" />
            </div>

            <!-- Provider-specific config -->
            <div class="space-y-2 border-t border-hairline pt-4">
              <h3 class="text-xs font-semibold uppercase tracking-wide text-ink-subtle">
                {{ activeProviderLabel }} Settings
              </h3>
              <BrevoConfigForm v-if="config.provider === 'brevo'" v-model="config.brevo" :errors="brevoFieldErrors" />
              <SmtpConfigForm v-else-if="config.provider === 'smtp'" v-model="config.smtp" :errors="smtpFieldErrors" />
            </div>
          </template>

          <!-- No-provider notice -->
          <div v-else class="border-t border-hairline pt-4">
            <p class="text-sm italic text-ink-muted">
              Outgoing email is disabled. Users will not receive email notifications or invitations.
            </p>
          </div>

          <!-- Save error -->
          <div
            v-if="saveError"
            class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
          >
            {{ saveError }}
          </div>

          <!-- Actions -->
          <div class="flex items-center gap-3 border-t border-hairline pt-4">
            <BaseButton :loading="saving" :disabled="saving" @click="save">
              {{ saving ? 'Saving…' : 'Save' }}
            </BaseButton>
            <BaseButton variant="secondary" :disabled="saving" @click="reset">
              Reset
            </BaseButton>
          </div>

          <!-- ── Test email ───────────────────────────────────────────────── -->
          <div
            v-if="serverSnapshot.provider !== null"
            class="space-y-3 border-t border-hairline pt-4"
          >
            <div>
              <h3 class="text-sm font-medium text-ink-strong">Send Test Email</h3>
              <p class="mt-0.5 text-xs text-ink-subtle">
                Verify the saved configuration can reach the provider by sending a test message.
              </p>
            </div>
            <div class="flex items-start gap-2">
              <div class="flex-1">
                <BaseInput
                  v-model="testRecipient"
                  type="email"
                  placeholder="recipient@example.com"
                  :disabled="testing"
                />
              </div>
              <BaseButton
                variant="secondary"
                :icon="IconSend"
                :loading="testing"
                :disabled="testing || !testRecipient.trim()"
                @click="sendTest"
              >
                {{ testing ? 'Sending…' : 'Send Test' }}
              </BaseButton>
            </div>
            <!-- Test result -->
            <div
              v-if="testResult"
              class="flex items-start gap-2 rounded-card px-4 py-3 text-sm"
              :class="testResult.success
                ? 'border border-success/25 bg-success-soft text-success'
                : 'border border-danger/20 bg-danger-soft text-danger'"
            >
              <IconCircleCheck v-if="testResult.success" class="mt-0.5 h-4 w-4 shrink-0" />
              <IconAlertCircle v-else class="mt-0.5 h-4 w-4 shrink-0" />
              <div>
                <span v-if="testResult.success">
                  Test email sent successfully to <strong>{{ testRecipient }}</strong>.
                </span>
                <template v-else>
                  <span>Failed to send test email.</span>
                  <div
                    v-if="testResult.providerError"
                    class="mt-1 break-all font-mono text-xs opacity-80"
                  >
                    {{ testResult.providerError }}
                  </div>
                </template>
              </div>
            </div>
          </div>

        </div>
      </BaseCard>
    </template>
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
import { BaseButton, BaseCard, BaseInput } from '@/shared/components/ui'
import { useToast } from '@/composables/useToast'

const toast = useToast()

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
    toast.success('Email configuration saved.')
  } catch (e: unknown) {
    const parsed = parseApiValidationError(e, 'Failed to save email configuration.')
    saveError.value = parsed.generalError
    fieldErrors.value = parsed.fieldErrors
    toast.error(parsed.generalError || 'Failed to save email configuration.')
  } finally {
    saving.value = false
  }
}

async function sendTest() {
  if (!testRecipient.value.trim()) return
  testResult.value = null
  testing.value = true
  try {
    testResult.value = await sendTestEmail(testRecipient.value.trim())
    if (testResult.value?.success) {
      toast.success(`Test email sent to ${testRecipient.value.trim()}.`)
    } else {
      toast.error('Failed to send test email.')
    }
  } catch (e: any) {
    testResult.value = { success: false, providerError: e.message || 'Unexpected error.' }
    toast.error('Failed to send test email.')
  } finally {
    testing.value = false
  }
}

onMounted(load)
</script>
