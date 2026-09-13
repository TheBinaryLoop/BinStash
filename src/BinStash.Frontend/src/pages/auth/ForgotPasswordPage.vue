<script setup lang="ts">
import { MailCheck } from '@lucide/vue'
import { ref } from 'vue'

import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { errorMessage } from '@/lib/errors'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()

const email = ref('')
const sent = ref(false)
const error = ref('')
const busy = ref(false)

async function submit() {
  busy.value = true
  error.value = ''

  try {
    await auth.forgotPassword(email.value)
  } catch (caught) {
    error.value = errorMessage(caught, 'Could not send the reset email.')
    return
  } finally {
    busy.value = false
  }

  // Always confirm, whether or not the address exists — the response must not
  // reveal which emails are registered.
  sent.value = true
}
</script>

<template>
  <div class="space-y-6">
    <template v-if="sent">
      <div class="space-y-3 text-center">
        <div
          class="bg-success/10 text-success mx-auto flex size-11 items-center justify-center rounded-lg"
        >
          <MailCheck class="size-5" />
        </div>
        <h1 class="text-xl font-semibold tracking-tight">Check your inbox</h1>
        <p class="text-muted-foreground text-sm">
          If an account exists for <span class="font-medium">{{ email }}</span
          >, a reset link is on its way.
        </p>
      </div>
      <Button variant="outline" class="w-full" @click="$router.push({ name: 'sign-in' })">
        Back to sign in
      </Button>
    </template>

    <template v-else>
      <div class="space-y-1.5">
        <h1 class="text-xl font-semibold tracking-tight">Reset your password</h1>
        <p class="text-muted-foreground text-sm">
          Enter your email and we will send you a reset link.
        </p>
      </div>

      <Alert v-if="error" variant="destructive">
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <form class="space-y-4" @submit.prevent="submit">
        <div class="space-y-2">
          <Label for="email">Email</Label>
          <Input
            id="email"
            v-model.trim="email"
            type="email"
            autocomplete="username"
            required
            :disabled="busy"
          />
        </div>
        <Button type="submit" class="w-full" :disabled="busy">
          {{ busy ? 'Sending…' : 'Send reset link' }}
        </Button>
      </form>

      <p class="text-center">
        <RouterLink
          :to="{ name: 'sign-in' }"
          class="text-muted-foreground hover:text-foreground text-sm"
        >
          Back to sign in
        </RouterLink>
      </p>
    </template>
  </div>
</template>
