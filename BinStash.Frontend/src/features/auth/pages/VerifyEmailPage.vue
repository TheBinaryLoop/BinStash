<template>
  <div>
    <AuthPageHeader
      title="Verify your email"
      description="This page confirms your email address and activates your account."
    />

    <AuthCard :padded="true">
      <Transition name="status-swap" mode="out-in">
        <div v-if="status.kind === 'verifying'" key="verifying">
          <div class="mb-4">
            <div class="bg-slate-500/20 text-slate-700 dark:text-slate-200 px-3 py-2 rounded-lg">
              <span class="text-sm">Verifying your email…</span>
            </div>
          </div>

          <div class="h-10 flex items-center">
            <div class="h-5 w-5 rounded-full border-2 border-violet-500 border-t-transparent animate-spin"></div>
          </div>
        </div>

        <div v-else-if="status.kind === 'success'" key="success">
          <div class="mb-4">
            <div class="bg-emerald-500/20 text-emerald-700 dark:text-emerald-200 px-3 py-2 rounded-lg">
              <span class="text-sm">{{ status.message }}</span>
            </div>
          </div>

          <div class="space-y-3">
            <router-link
              class="btn w-full bg-violet-500 text-white hover:bg-violet-600 dark:bg-violet-500 dark:hover:bg-violet-600 text-center"
              :to="signinLink"
            >
              Continue to sign in
            </router-link>
            <router-link
              class="btn w-full border-gray-200 hover:border-gray-300 dark:border-gray-700/60 dark:hover:border-gray-600 text-gray-800 dark:text-gray-100"
              to="/"
            >
              Back to home
            </router-link>
          </div>
        </div>

        <div v-else-if="status.kind === 'error'" key="error">
          <div class="mb-4">
            <div class="bg-rose-500/20 text-rose-700 dark:text-rose-200 px-3 py-2 rounded-lg">
              <span class="text-sm">{{ status.message }}</span>
            </div>
          </div>

          <div class="space-y-3">
            <button
              class="btn w-full bg-violet-500 text-white hover:bg-violet-600 dark:bg-violet-500 dark:hover:bg-violet-600"
              type="button"
              @click="verifyFromLink"
            >
              Try again
            </button>
            <router-link
              class="btn w-full border-gray-200 hover:border-gray-300 dark:border-gray-700/60 dark:hover:border-gray-600 text-gray-800 dark:text-gray-100"
              :to="signinLink"
            >
              Back to sign in
            </router-link>
          </div>
        </div>

        <div v-else key="idle">
          <div class="mb-4">
            <div class="bg-sky-500/20 text-sky-700 dark:text-sky-200 px-3 py-2 rounded-lg">
              <span class="text-sm">This verification link is incomplete or invalid.</span>
            </div>
          </div>

          <router-link
            class="btn w-full border-gray-200 hover:border-gray-300 dark:border-gray-700/60 dark:hover:border-gray-600 text-gray-800 dark:text-gray-100"
            :to="signinLink"
          >
            Back to sign in
          </router-link>
        </div>
      </Transition>
    </AuthCard>
  </div>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute } from 'vue-router'
import { apiFetch, throwForStatus } from '@/shared/api/http'
import AuthCard from '@/shared/components/auth/AuthCard.vue'
import AuthPageHeader from '@/shared/components/auth/AuthPageHeader.vue'

const CONFIRM_ENDPOINT = '/api/auth/confirmEmail'

const route = useRoute()

type Status =
  | { kind: 'idle' }
  | { kind: 'verifying' }
  | { kind: 'success'; message: string }
  | { kind: 'error'; message: string }

const status = ref<Status>({ kind: 'idle' })
const userId = computed(() => (typeof route.query.userId === 'string' ? route.query.userId : null))
const code = computed(() => (typeof route.query.code === 'string' ? route.query.code : null))
const changedEmail = computed(() => (typeof route.query.changedEmail === 'string' ? route.query.changedEmail : null))
const redirect = computed(() => (typeof route.query.redirect === 'string' ? route.query.redirect : undefined))
const signinLink = computed(() => ({ path: '/signin', query: redirect.value ? { redirect: redirect.value } : undefined }))

/**
 * Expected verification link shape:
 * /verify-email?userId=...&code=...(&changedEmail=...)
 */
async function verifyFromLink() {
  if (!userId.value || !code.value) {
    status.value = {
      kind: 'error',
      message: 'Invalid verification link. Required parameters are missing.',
    }
    return
  }

  status.value = { kind: 'verifying' }

  try {
    const params = new URLSearchParams({
      userId: userId.value,
      code: code.value,
    })

    if (changedEmail.value) {
      params.set('changedEmail', changedEmail.value)
    }

    const res = await apiFetch(`${CONFIRM_ENDPOINT}?${params.toString()}`, { method: 'GET' })
    await throwForStatus(res)

    const serverMessage = await safeReadText(res)
    status.value = {
      kind: 'success',
      message: serverMessage || 'Your email has been confirmed successfully. You can now sign in.',
    }
  } catch (e) {
    status.value = {
      kind: 'error',
      message: e instanceof Error ? e.message : 'Email verification failed.',
    }
  }
}

onMounted(async () => {
  await verifyFromLink()
})

async function safeReadText(res: Response): Promise<string> {
  try {
    return (await res.text()).trim()
  } catch {
    return ''
  }
}
</script>

<style scoped>
.status-swap-enter-active,
.status-swap-leave-active {
  transition: opacity 220ms ease, transform 240ms ease;
}

.status-swap-enter-from,
.status-swap-leave-to {
  opacity: 0;
  transform: translateY(8px) scale(0.99);
}
</style>
