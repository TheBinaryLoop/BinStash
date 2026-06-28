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
                <div>
                  <label class="block text-sm font-medium mb-1" for="first-name">First name</label>
                  <input
                    id="first-name"
                    class="form-input w-full"
                    type="text"
                    v-model.trim="firstName"
                    :disabled="isSubmitting"
                    required
                  />
                </div>
                <div>
                  <label class="block text-sm font-medium mb-1" for="last-name">Last name</label>
                  <input
                    id="last-name"
                    class="form-input w-full"
                    type="text"
                    v-model.trim="lastName"
                    :disabled="isSubmitting"
                    required
                  />
                </div>
              </div>

              <div class="mt-4">
                <label class="block text-sm font-medium mb-1" for="middle-name">Middle name (optional)</label>
                <input
                  id="middle-name"
                  class="form-input w-full"
                  type="text"
                  v-model.trim="middleName"
                  :disabled="isSubmitting"
                />
              </div>

              <div class="mt-4">
                <label class="block text-sm font-medium mb-1" for="email">Email address</label>
                <div class="relative">
                  <input
                    id="email"
                    class="form-input w-full"
                    :class="isInvitationEmailLocked ? 'bg-gray-100 dark:bg-gray-700 border-dashed border-gray-300 dark:border-gray-600 text-gray-500 dark:text-gray-300 cursor-not-allowed pr-10' : ''"
                    type="email"
                    autocomplete="email"
                    v-model.trim="email"
                    :disabled="isSubmitting || isInvitationEmailLocked"
                    required
                  />
                </div>
                <p v-if="isInvitationEmailLocked" class="mt-1 text-xs text-gray-500 dark:text-gray-400">
                  This email is fixed by the invitation and cannot be changed.
                </p>
              </div>

              <div class="mt-4">
                <label class="block text-sm font-medium mb-1" for="password">Password</label>
                <input
                  id="password"
                  class="form-input w-full"
                  type="password"
                  autocomplete="new-password"
                  minlength="8"
                  v-model="password"
                  :disabled="isSubmitting"
                  required
                />
                <p class="mt-1 text-xs" :class="password.length === 0 ? 'text-gray-500 dark:text-gray-400' : (isPasswordLongEnough ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400')">
                  {{ password.length === 0 ? 'Use at least 8 characters.' : (isPasswordLongEnough ? 'Password length looks good.' : 'Password must be at least 8 characters.') }}
                </p>
              </div>

              <div class="mt-4">
                <label class="block text-sm font-medium mb-1" for="confirm-password">Confirm password</label>
                <input
                  id="confirm-password"
                  class="form-input w-full"
                  type="password"
                  autocomplete="new-password"
                  minlength="8"
                  v-model="confirmPassword"
                  :disabled="isSubmitting"
                  required
                />
                <p v-if="confirmPassword.length > 0" class="mt-1 text-xs" :class="passwordsMatch ? 'text-emerald-600 dark:text-emerald-400' : 'text-rose-600 dark:text-rose-400'">
                  {{ passwordsMatch ? 'Passwords match.' : 'Passwords do not match.' }}
                </p>
              </div>

              <div class="mt-4">
                <label class="flex items-center">
                  <input
                    type="checkbox"
                    class="form-checkbox"
                    v-model="acceptTerms"
                    :disabled="isSubmitting"
                  />
                  <span class="text-sm ml-2 text-gray-600 dark:text-gray-400">
                    I agree to the terms and privacy policy.
                  </span>
                </label>
              </div>

              <div class="mt-6">
                <button
                  class="btn w-full bg-violet-500 text-white hover:bg-violet-600 dark:bg-violet-500 dark:hover:bg-violet-600"
                  type="submit"
                  :disabled="!canSubmit"
                >
                  <span v-if="!isSubmitting">Create account</span>
                  <span v-else>Creating account…</span>
                </button>
              </div>
            </form>

            <div class="pt-5 mt-6 border-t border-gray-200 dark:border-gray-700/60">
              <div class="text-sm">
                Already have an account?
                <router-link
                  class="font-medium text-violet-500 hover:text-violet-600 dark:hover:text-violet-400"
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
            class="relative overflow-hidden rounded-xl border border-emerald-200/70 dark:border-emerald-700/40 bg-linear-to-br from-emerald-50 via-white to-violet-50 dark:from-emerald-900/20 dark:via-gray-800 dark:to-violet-900/10 p-6 sm:p-7"
          >
            <div class="pointer-events-none absolute -top-16 -right-16 h-40 w-40 rounded-full bg-violet-400/20 blur-2xl"></div>
            <div class="pointer-events-none absolute -bottom-14 -left-14 h-36 w-36 rounded-full bg-emerald-400/20 blur-2xl"></div>

            <div class="relative z-10">
              <div class="mb-4 flex items-center gap-3">
                <div class="relative flex h-12 w-12 items-center justify-center rounded-full bg-emerald-500/15">
                  <span class="absolute inline-flex h-full w-full rounded-full bg-emerald-400/40 animate-ping"></span>
                  <svg class="relative h-6 w-6 text-emerald-600 dark:text-emerald-300" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" aria-hidden="true">
                    <path stroke-linecap="round" stroke-linejoin="round" d="m5 13 4 4L19 7" />
                  </svg>
                </div>
                <div>
                  <p class="text-lg font-semibold text-gray-900 dark:text-gray-100">Account created successfully</p>
                  <p class="text-sm text-gray-600 dark:text-gray-300">{{ successMessage }}</p>
                </div>
              </div>

              <div class="rounded-lg border border-white/60 dark:border-gray-700/80 bg-white/80 dark:bg-gray-800/80 backdrop-blur px-4 py-3 mb-4">
                <p class="text-xs uppercase tracking-wide font-semibold text-gray-500 dark:text-gray-400 mb-2">Next steps</p>
                <ol class="space-y-2.5">
                  <li class="flex gap-3" v-for="step in successSteps" :key="step.title">
                    <span class="mt-0.5 inline-flex h-5 w-5 shrink-0 items-center justify-center rounded-full bg-violet-500/15 text-[11px] font-semibold text-violet-700 dark:text-violet-300">{{ step.index }}</span>
                    <span class="text-sm text-gray-700 dark:text-gray-200">
                      <span class="font-medium">{{ step.title }}:</span>
                      {{ step.description }}
                    </span>
                  </li>
                </ol>
              </div>

              <p v-if="registeredEmail" class="text-xs text-gray-500 dark:text-gray-400 mb-4">
                Verification email sent to <span class="font-medium text-gray-700 dark:text-gray-200">{{ registeredEmail }}</span>
              </p>

              <div class="flex flex-col sm:flex-row gap-3">
                <router-link
                  class="btn bg-violet-500 text-white hover:bg-violet-600 dark:bg-violet-500 dark:hover:bg-violet-600 text-center"
                  :to="signinLink"
                >
                  Continue to Sign In
                </router-link>
                <router-link
                  class="btn bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-700 text-gray-800 dark:text-gray-200 text-center"
                  to="/"
                >
                  Back to Home
                </router-link>
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
import AuthCard from '@/shared/components/auth/AuthCard.vue'
import AuthPageHeader from '@/shared/components/auth/AuthPageHeader.vue'
import AuthAlert from '@/shared/components/auth/AuthAlert.vue'

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