<script setup lang="ts">
import { CircleCheck, CircleX, MailCheck } from '@lucide/vue'
import { onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { Button } from '@/components/ui/button'
import { errorMessage } from '@/lib/errors'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const route = useRoute()
const router = useRouter()

type State = 'pending' | 'confirming' | 'confirmed' | 'failed'

const state = ref<State>('pending')
const message = ref('')
const resent = ref(false)
const email = ref(typeof route.query.email === 'string' ? route.query.email : '')

onMounted(async () => {
  const userId = route.query.userId
  const code = route.query.code

  // Arriving from the emailed link carries the confirmation params; otherwise this page
  // is just the "we sent you a mail" state.
  if (typeof userId !== 'string' || typeof code !== 'string') return

  state.value = 'confirming'
  try {
    await auth.confirmEmail(userId, code)
    await auth.refresh()
    state.value = 'confirmed'
  } catch (caught) {
    state.value = 'failed'
    message.value = errorMessage(caught, 'This confirmation link is invalid or has expired.')
  }
})

async function resend() {
  if (!email.value) return
  await auth.resendConfirmationEmail(email.value)
  resent.value = true
}
</script>

<template>
  <div class="space-y-6 text-center">
    <template v-if="state === 'confirmed'">
      <div class="bg-success/10 text-success mx-auto flex size-11 items-center justify-center rounded-lg">
        <CircleCheck class="size-5" />
      </div>
      <div class="space-y-1.5">
        <h1 class="text-xl font-semibold tracking-tight">Email confirmed</h1>
        <p class="text-muted-foreground text-sm">Your account is ready to use.</p>
      </div>
      <Button class="w-full" @click="router.push({ name: 'select-tenant' })">Continue</Button>
    </template>

    <template v-else-if="state === 'failed'">
      <div class="bg-destructive/10 text-destructive mx-auto flex size-11 items-center justify-center rounded-lg">
        <CircleX class="size-5" />
      </div>
      <div class="space-y-1.5">
        <h1 class="text-xl font-semibold tracking-tight">Confirmation failed</h1>
        <p class="text-muted-foreground text-sm">{{ message }}</p>
      </div>
      <Button v-if="email" variant="outline" class="w-full" :disabled="resent" @click="resend">
        {{ resent ? 'Confirmation email sent' : 'Send a new confirmation email' }}
      </Button>
    </template>

    <template v-else-if="state === 'confirming'">
      <p class="text-muted-foreground text-sm">Confirming your email…</p>
    </template>

    <template v-else>
      <div class="bg-primary/10 text-primary mx-auto flex size-11 items-center justify-center rounded-lg">
        <MailCheck class="size-5" />
      </div>
      <div class="space-y-1.5">
        <h1 class="text-xl font-semibold tracking-tight">Confirm your email</h1>
        <p class="text-muted-foreground text-sm">
          We sent a confirmation link<template v-if="email">
            to <span class="font-medium">{{ email }}</span></template
          >. Follow it to activate your account.
        </p>
      </div>
      <Button v-if="email" variant="outline" class="w-full" :disabled="resent" @click="resend">
        {{ resent ? 'Confirmation email sent' : 'Resend confirmation email' }}
      </Button>
      <RouterLink
        :to="{ name: 'sign-in' }"
        class="text-muted-foreground hover:text-foreground block text-sm"
      >
        Back to sign in
      </RouterLink>
    </template>
  </div>
</template>
