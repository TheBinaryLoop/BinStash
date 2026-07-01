<template>
  <div>
    <AuthPageHeader
      :title="pageTitle"
      :description="success ? 'You\'re almost done. Confirm your email and continue in BinStash.' : hasValidInvitation ? 'You were invited to join a workspace. Create your account to link this invitation during signup.' : 'Get started with BinStash and manage your releases in one place.'"
    />

    <AuthCard :padded="true">
      <Transition name="success-swap" mode="out-in">
          <div v-if="!success" key="signup-form">
            <div v-if="hasValidInvitation" class="mb-4">
              <AuthAlert tone="info">
                <span v-if="isLoadingInvitationPreview">
                  Invitation detected. Loading workspace details…
                </span>

                <span v-else-if="invitationPreview">
                  You are joining
                  <span class="font-semibold">
                    {{ invitationPreview.tenantName || 'a workspace' }}
                  </span>
                  as
                  <span class="font-semibold">
                    {{ invitationPreview.role || 'a member' }}
                  </span>.
                  This invitation will be linked when creating your account.
                </span>

                <span v-else>
                  Invitation detected. We'll include it when creating your account.
                </span>
              </AuthAlert>
            </div>

            <div v-if="invitationError" class="mb-4">
              <AuthAlert tone="error">
                {{ invitationError }}
              </AuthAlert>
            </div>

            <div v-if="error" class="mb-4">
              <AuthAlert tone="error">
                {{ error }}
              </AuthAlert>
            </div>

            <form @submit.prevent="onSubmit">
              <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
                <BaseInput
                  v-model.trim="firstName"
                  label="First name"
                  type="text"
                  :disabled="isSubmitting"
                  required
                />
                <BaseInput
                  v-model.trim="lastName"
                  label="Last name"
                  type="text"
                  :disabled="isSubmitting"
                  required
                />
              </div>

              <div class="mt-4">
                <BaseInput
                  v-model.trim="middleName"
                  label="Middle name (optional)"
                  type="text"
                  :disabled="isSubmitting"
                />
              </div>

              <div class="mt-4">
                <BaseInput
                  v-model.trim="email"
                  label="Email address"
                  type="email"
                  autocomplete="email"
                  :disabled="isSubmitting || isInvitationEmailLocked"
                  :hint="isInvitationEmailLocked ? 'This email is fixed by the invitation and cannot be changed.' : undefined"
                  required
                />
              </div>

              <div class="mt-4">
                <BaseInput
                  v-model="password"
                  label="Password"
                  type="password"
                  autocomplete="new-password"
                  :disabled="isSubmitting"
                  required
                />
                <p class="mt-1 text-xs" :class="password.length === 0 ? 'text-ink-muted' : (isPasswordLongEnough ? 'text-success' : 'text-danger')">
                  {{ password.length === 0 ? 'Use at least 8 characters.' : (isPasswordLongEnough ? 'Password length looks good.' : 'Password must be at least 8 characters.') }}
                </p>
              </div>

              <div class="mt-4">
                <BaseInput
                  v-model="confirmPassword"
                  label="Confirm password"
                  type="password"
                  autocomplete="new-password"
                  :disabled="isSubmitting"
                  required
                />
                <p v-if="confirmPassword.length > 0" class="mt-1 text-xs" :class="passwordsMatch ? 'text-success' : 'text-danger'">
                  {{ passwordsMatch ? 'Passwords match.' : 'Passwords do not match.' }}
                </p>
              </div>

              <div class="mt-4">
                <BaseCheckbox
                  v-model="acceptTerms"
                  label="I agree to the terms and privacy policy."
                  :disabled="isSubmitting"
                />
              </div>

              <div class="mt-6">
                <BaseButton type="submit" block :loading="isSubmitting" :disabled="!canSubmit">
                  {{ isSubmitting ? 'Creating account…' : 'Create account' }}
                </BaseButton>
              </div>
            </form>

            <div class="pt-5 mt-6 border-t border-hairline">
              <div class="text-sm text-ink-muted">
                Already have an account?
                <router-link
                  class="font-medium text-accent transition hover:brightness-110"
                  :to="signinLink"
                >
                  Sign In
                </router-link>
              </div>
            </div>
          </div>

          <div
            v-else
            key="signup-success"
            class="relative overflow-hidden rounded-card border border-success/25 bg-success-soft p-6 sm:p-7"
          >
            <div class="pointer-events-none absolute -top-16 -right-16 h-40 w-40 rounded-full bg-accent/20 blur-2xl"></div>
            <div class="pointer-events-none absolute -bottom-14 -left-14 h-36 w-36 rounded-full bg-success/20 blur-2xl"></div>

            <div class="relative z-10">
              <div class="mb-4 flex items-center gap-3">
                <div class="relative flex h-12 w-12 items-center justify-center rounded-full bg-success/15">
                  <span class="absolute inline-flex h-full w-full rounded-full bg-success/40 animate-ping"></span>
                  <IconCheck class="relative h-6 w-6 text-success" />
                </div>
                <div>
                  <p class="text-lg font-semibold text-ink-strong">Account created successfully</p>
                  <p class="text-sm text-ink-muted">{{ successMessage }}</p>
                </div>
              </div>

              <div class="rounded-control border border-hairline bg-card/80 backdrop-blur px-4 py-3 mb-4">
                <p class="text-xs uppercase tracking-wide font-semibold text-ink-subtle mb-2">Next steps</p>
                <ol class="space-y-2.5">
                  <li class="flex gap-3" v-for="step in successSteps" :key="step.title">
                    <span class="mt-0.5 inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-accent-soft text-[11px] font-semibold text-accent">{{ step.index }}</span>
                    <span class="text-sm text-ink-muted">
                      <span class="font-medium text-ink-strong">{{ step.title }}:</span>
                      {{ step.description }}
                    </span>
                  </li>
                </ol>
              </div>

              <p v-if="registeredEmail" class="text-xs text-ink-muted mb-4">
                Verification email sent to <span class="font-medium text-ink-strong">{{ registeredEmail }}</span>
              </p>

              <div class="flex flex-col sm:flex-row gap-3">
                <BaseButton :to="signinLink">Continue to Sign In</BaseButton>
                <BaseButton variant="secondary" to="/">Back to Home</BaseButton>
              </div>
            </div>
        </div>
      </Transition>
    </AuthCard>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { apiFetch, throwForStatus } from '../../../shared/api/http'
import { previewTenantInvitation, type InvitationPreviewDto } from '../../../api/tenants'
import { IconCheck } from '@tabler/icons-vue'
import AuthCard from '@/shared/components/auth/AuthCard.vue'
import AuthPageHeader from '@/shared/components/auth/AuthPageHeader.vue'
import AuthAlert from '@/shared/components/auth/AuthAlert.vue'
import { BaseInput, BaseCheckbox, BaseButton } from '@/shared/components/ui'

const route = useRoute()

const firstName = ref('')
const middleName = ref('')
const lastName = ref('')
const email = ref('')
const password = ref('')
const confirmPassword = ref('')
const acceptTerms = ref(false)

const isSubmitting = ref(false)
const success = ref(false)
const error = ref<string | null>(null)
const registeredEmail = ref('')
const successMessage = ref(
  'Account created. Please verify your email and sign in to complete onboarding before using BinStash.'
)
const invitationPreview = ref<InvitationPreviewDto | null>(null)
const isLoadingInvitationPreview = ref(false)
const invitationError = ref<string | null>(null)
const hasValidInvitation = ref(false)

const invitationCode = computed(() => {
  const value = route.query.invitationCode
  return typeof value === 'string' && value.length > 0 ? value : null
})

const tenantId = computed(() => {
  const value = route.query.tenantId
  return typeof value === 'string' && value.length > 0 ? value : null
})

const activeInvitationCode = computed(() => (hasValidInvitation.value ? invitationCode.value : null))

const lockedInvitationEmail = computed(() => {
  const value = invitationPreview.value?.invitedEmail
  return typeof value === 'string' && value.length > 0 ? value : null
})

const isInvitationEmailLocked = computed(() => !!lockedInvitationEmail.value)

const isPasswordLongEnough = computed(() => password.value.length >= 8)
const passwordsMatch = computed(() => password.value === confirmPassword.value)
const isEmailValid = computed(() => /^\S+@\S+\.\S+$/.test(email.value.trim()))

const isFormValid = computed(() => {
  return (
    firstName.value.trim().length > 0 &&
    lastName.value.trim().length > 0 &&
    isEmailValid.value &&
    isPasswordLongEnough.value &&
    confirmPassword.value.length > 0 &&
    passwordsMatch.value &&
    acceptTerms.value
  )
})

const canSubmit = computed(() => !isSubmitting.value && !success.value && isFormValid.value)
const pageTitle = computed(() => (success.value ? 'Check your inbox' : 'Create your account'))
const successSteps = [
  {
    index: 1,
    title: 'Verify your email',
    description: 'Open the verification link we just sent to activate your account.',
  },
  {
    index: 2,
    title: 'Sign in',
    description: 'Return to BinStash and sign in with your new credentials.',
  },
  {
    index: 3,
    title: 'Finish onboarding',
    description: 'Complete the quick onboarding flow to start working right away.',
  },
]

const authFlowQuery = computed<Record<string, string>>(() => {
  const query: Record<string, string> = {}

  if (!hasValidInvitation.value && typeof route.query.redirect === 'string' && route.query.redirect.length > 0) {
    query.redirect = route.query.redirect
  }

  return query
})

const signinLink = computed(() => ({ path: '/signin', query: authFlowQuery.value }))

async function loadInvitationPreview() {
  if (!tenantId.value || !invitationCode.value) return
  isLoadingInvitationPreview.value = true
  invitationError.value = null
  hasValidInvitation.value = false
  try {
    invitationPreview.value = await previewTenantInvitation(tenantId.value, invitationCode.value)
    hasValidInvitation.value = true
    if (lockedInvitationEmail.value) {
      email.value = lockedInvitationEmail.value
    }
  } catch {
    invitationPreview.value = null
    invitationError.value = 'This invitation link is invalid or has expired. You can continue with regular signup.'
    hasValidInvitation.value = false
  } finally {
    isLoadingInvitationPreview.value = false
  }
}

onMounted(loadInvitationPreview)

async function onSubmit() {
  error.value = null

  if (!isFormValid.value) {
    error.value = 'Please complete all required fields and fix validation errors.'
    return
  }

  if (password.value !== confirmPassword.value) {
    error.value = 'Passwords do not match.'
    return
  }

  if (!acceptTerms.value) {
    error.value = 'You must accept the terms and privacy policy to continue.'
    return
  }

  isSubmitting.value = true
  try {
    if (lockedInvitationEmail.value && email.value.toLowerCase() !== lockedInvitationEmail.value.toLowerCase()) {
      email.value = lockedInvitationEmail.value
      error.value = 'The email address is fixed by this invitation and cannot be changed.'
      return
    }

    const payload: {
      firstName: string
      middleName: string | null
      lastName: string
      email: string
      password: string
      invitationCode?: string
    } = {
      firstName: firstName.value,
      middleName: middleName.value.length > 0 ? middleName.value : null,
      lastName: lastName.value,
      email: email.value,
      password: password.value,
    }

    if (activeInvitationCode.value) {
      payload.invitationCode = activeInvitationCode.value
    }

    const res = await apiFetch('/api/auth/register', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(payload),
    })

    await throwForStatus(res)
    registeredEmail.value = email.value
    success.value = true
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Sign up failed.'
  } finally {
    isSubmitting.value = false
  }
}
</script>

<style scoped>
.success-swap-enter-active,
.success-swap-leave-active {
  transition: opacity 240ms ease, transform 260ms ease;
}

.success-swap-enter-from,
.success-swap-leave-to {
  opacity: 0;
  transform: translateY(10px) scale(0.985);
}
</style>