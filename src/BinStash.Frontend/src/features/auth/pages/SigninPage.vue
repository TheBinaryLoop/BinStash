<template>
  <div>
    <AuthPageHeader
      title="Welcome back"
      :description="hasValidInvitation ? 'Sign in to review and decide whether to accept your workspace invitation.' : undefined"
    />

    <AuthCard :padded="true">
      <div v-if="hasValidInvitation" class="mb-4">
        <AuthAlert tone="info">
          Invitation detected. You'll be taken to the invite page after sign in.
        </AuthAlert>
      </div>

      <div v-if="invitationError" class="mb-4">
        <AuthAlert tone="error" :message="invitationError" />
      </div>

      <div v-if="error" class="mb-4">
        <AuthAlert tone="error" :message="error" />
      </div>

      <form @submit.prevent="onSubmit">
        <div class="space-y-4">
          <div>
            <label class="mb-1.5 block text-sm font-medium text-ink-strong" for="email">Email Address</label>
            <input
              id="email"
              class="h-11 w-full rounded-2xl border border-hairline bg-transparent px-4 text-sm text-ink-strong outline-none transition placeholder:text-ink-muted focus:border-accent disabled:opacity-50"
              type="email"
              autocomplete="email"
              v-model.trim="email"
              :disabled="isSubmitting"
              required
            />
          </div>

          <div>
            <label class="mb-1.5 block text-sm font-medium text-ink-strong" for="password">Password</label>
            <input
              id="password"
              class="h-11 w-full rounded-2xl border border-hairline bg-transparent px-4 text-sm text-ink-strong outline-none transition placeholder:text-ink-muted focus:border-accent disabled:opacity-50"
              type="password"
              autocomplete="current-password"
              v-model="password"
              :disabled="isSubmitting"
              required
            />
          </div>
        </div>

        <div class="mt-4 flex items-center">
          <input
            id="stay-signed-in"
            type="checkbox"
            class="size-4 rounded border-hairline text-accent focus:ring-accent"
            v-model="staySignedIn"
            :disabled="isSubmitting"
          />
          <label for="stay-signed-in" class="ml-2 text-sm text-ink-muted">
            Stay signed in
          </label>
        </div>

        <div class="mt-6 flex items-center justify-between gap-3">
          <router-link class="text-sm text-ink-muted underline-offset-2 transition hover:text-ink-strong hover:underline" to="/reset-password">
            Forgot Password?
          </router-link>

          <button
            class="inline-flex items-center rounded-full bg-accent px-5 py-2.5 text-sm font-semibold text-white whitespace-nowrap transition hover:brightness-110 disabled:opacity-50"
            type="submit"
            :disabled="isSubmitting"
          >
            <span v-if="!isSubmitting">Sign In</span>
            <span v-else>Signing in…</span>
          </button>
        </div>
      </form>

      <div class="mt-6 border-t border-hairline pt-5">
        <div class="text-sm text-ink-muted">
          Don’t have an account?
          <router-link
            class="font-medium text-accent transition hover:brightness-110"
            :to="{ path: '/signup', query: authFlowQuery }"
          >
            Sign Up
          </router-link>
        </div>
      </div>
    </AuthCard>
  </div>
</template>

<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { listTenantsForMember, previewTenantInvitation } from '../../../api/tenants'
import { useAuthStore } from '../../../stores/auth'
import { useTenantStore } from '../../../stores/tenant'
import AuthCard from '@/shared/components/auth/AuthCard.vue'
import AuthPageHeader from '@/shared/components/auth/AuthPageHeader.vue'
import AuthAlert from '@/shared/components/auth/AuthAlert.vue'

const auth = useAuthStore()
const tenant = useTenantStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')
const staySignedIn = ref(false)
const isSubmitting = ref(false)
const error = ref<string | null>(null)
const invitationError = ref<string | null>(null)
const hasValidInvitation = ref(false)

const invitationCode =
  typeof route.query.invitationCode === 'string' && route.query.invitationCode.length > 0
    ? route.query.invitationCode
    : null
const tenantId =
  typeof route.query.tenantId === 'string' && route.query.tenantId.length > 0
    ? route.query.tenantId
    : null

const activeInvitationCode = ref<string | null>(null)
const activeTenantId = ref<string | null>(null)

const authFlowQuery: Record<string, string> = {}
if (typeof route.query.redirect === 'string' && route.query.redirect.length > 0) {
  authFlowQuery.redirect = route.query.redirect
}
if (invitationCode && tenantId) {
  authFlowQuery.invitationCode = invitationCode
  authFlowQuery.tenantId = tenantId
}

async function validateInvitation() {
  if (!tenantId || !invitationCode) return
  invitationError.value = null
  hasValidInvitation.value = false
  try {
    await previewTenantInvitation(tenantId, invitationCode)
    hasValidInvitation.value = true
    activeInvitationCode.value = invitationCode
    activeTenantId.value = tenantId
  } catch {
    invitationError.value = 'This invitation link is invalid or has expired. Please sign in without invitation actions.'
    hasValidInvitation.value = false
    activeInvitationCode.value = null
    activeTenantId.value = null
    delete authFlowQuery.invitationCode
    delete authFlowQuery.tenantId
  }
}

onMounted(validateInvitation)

async function onSubmit() {
  error.value = null
  isSubmitting.value = true
  try {
    await auth.login(email.value, password.value, staySignedIn.value)

    // Instance admins go directly to the instance dashboard
    if (auth.isInstanceAdmin) {
      await router.replace('/instance')
      return
    }

    const tenants = await listTenantsForMember()
    tenant.setTenants(tenants)

    const redirect =
      typeof route.query.redirect === 'string' && route.query.redirect.length > 0
        ? route.query.redirect
        : null

    if (redirect) {
      await router.replace(redirect)
    } else if (activeInvitationCode.value && activeTenantId.value) {
      await router.replace(`/invite/${activeTenantId.value}/${activeInvitationCode.value}`)
    } else if (tenants.length === 1) {
      tenant.setCurrentTenant(tenants[0].tenantId)
      await router.replace(`/t/${tenants[0].tenantId}`)
    } else {
      await router.replace('/select-tenant')
    }
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Login failed'
  } finally {
    isSubmitting.value = false
  }
}
</script>
