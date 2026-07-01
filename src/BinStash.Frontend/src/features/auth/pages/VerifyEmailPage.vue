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
            <AuthAlert tone="info">Verifying your email…</AuthAlert>
          </div>

          <div class="h-10 flex items-center">
            <Spinner :size="20" :thickness="2" color="var(--color-accent)" />
          </div>
        </div>

        <div v-else-if="status.kind === 'success'" key="success">
          <div class="mb-4">
            <AuthAlert tone="success" :message="status.message" />
          </div>

          <div class="space-y-3">
            <BaseButton block :to="signinLink">Continue to sign in</BaseButton>
            <BaseButton block variant="secondary" to="/">Back to home</BaseButton>
          </div>
        </div>

        <div v-else-if="status.kind === 'error'" key="error">
          <div class="mb-4">
            <AuthAlert tone="error" :message="status.message" />
          </div>

          <div class="space-y-3">
            <BaseButton block type="button" @click="verifyFromLink">Try again</BaseButton>
            <BaseButton block variant="secondary" :to="signinLink">Back to sign in</BaseButton>
          </div>
        </div>

        <div v-else key="idle">
          <div class="mb-4">
            <AuthAlert tone="info" message="This verification link is incomplete or invalid." />
          </div>

          <BaseButton block variant="secondary" :to="signinLink">Back to sign in</BaseButton>
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
import AuthAlert from '@/shared/components/auth/AuthAlert.vue'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BaseButton } from '@/shared/components/ui'

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
