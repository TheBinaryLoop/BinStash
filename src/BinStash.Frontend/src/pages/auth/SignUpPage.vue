<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { errorMessage } from '@/lib/errors'
import { useAuthStore } from '@/stores/auth'

const auth = useAuthStore()
const router = useRouter()
const route = useRoute()

const firstName = ref('')
const lastName = ref('')
const email = ref('')
const password = ref('')
const error = ref('')
const busy = ref(false)

const invitationCode = typeof route.query.invitation === 'string' ? route.query.invitation : undefined

async function submit() {
  busy.value = true
  error.value = ''

  try {
    await auth.register({
      firstName: firstName.value,
      lastName: lastName.value,
      email: email.value,
      password: password.value,
      invitationCode,
    })
    // Registration always requires email confirmation before the account is usable.
    await router.push({ name: 'verify-email', query: { email: email.value } })
  } catch (caught) {
    error.value = errorMessage(caught, 'Could not create your account.')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="space-y-6">
    <div class="space-y-1.5">
      <h1 class="text-xl font-semibold tracking-tight">Create an account</h1>
      <p class="text-muted-foreground text-sm">
        You will receive a confirmation email before you can sign in.
      </p>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <form class="space-y-4" @submit.prevent="submit">
      <div class="grid grid-cols-2 gap-3">
        <div class="space-y-2">
          <Label for="first-name">First name</Label>
          <Input id="first-name" v-model.trim="firstName" required :disabled="busy" />
        </div>
        <div class="space-y-2">
          <Label for="last-name">Last name</Label>
          <Input id="last-name" v-model.trim="lastName" required :disabled="busy" />
        </div>
      </div>

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

      <div class="space-y-2">
        <Label for="password">Password</Label>
        <Input
          id="password"
          v-model="password"
          type="password"
          autocomplete="new-password"
          required
          :disabled="busy"
        />
      </div>

      <Button type="submit" class="w-full" :disabled="busy">
        {{ busy ? 'Creating account…' : 'Create account' }}
      </Button>
    </form>

    <p class="text-muted-foreground text-center text-sm">
      Already have an account?
      <RouterLink :to="{ name: 'sign-in' }" class="text-foreground font-medium hover:underline">
        Sign in
      </RouterLink>
    </p>
  </div>
</template>
