<script setup lang="ts">
import { ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Alert, AlertDescription } from '@/components/ui/alert'
import { errorMessage } from '@/lib/errors'
import { ApiError } from '@/lib/http'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'

const auth = useAuthStore()
const tenants = useTenantStore()
const router = useRouter()
const route = useRoute()

const email = ref('')
const password = ref('')
const twoFactorCode = ref('')
/** Only revealed once the server says the account has 2FA enabled. */
const needsTwoFactor = ref(false)
const error = ref('')
const busy = ref(false)

async function submit() {
  busy.value = true
  error.value = ''

  try {
    await auth.signIn(email.value, password.value, twoFactorCode.value || undefined)
    await tenants.load()

    const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : null
    await router.push(redirect ?? { name: 'select-tenant' })
  } catch (caught) {
    if (caught instanceof ApiError && /two.?factor/i.test(caught.message)) {
      needsTwoFactor.value = true
      error.value = 'Enter the code from your authenticator app.'
    } else if (caught instanceof ApiError && caught.status === 401) {
      // Deliberately vague: distinguishing "no such user" from "wrong password" is an
      // account-enumeration oracle.
      error.value = 'That email or password is not correct.'
    } else {
      error.value = errorMessage(caught, 'Could not sign you in.')
    }
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="space-y-6">
    <div class="space-y-1.5">
      <h1 class="text-xl font-semibold tracking-tight">Sign in</h1>
      <p class="text-muted-foreground text-sm">Sign in to your BinStash workspace.</p>
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

      <div class="space-y-2">
        <div class="flex items-baseline justify-between">
          <Label for="password">Password</Label>
          <RouterLink
            :to="{ name: 'forgot-password' }"
            class="text-muted-foreground hover:text-foreground text-xs"
          >
            Forgot your password?
          </RouterLink>
        </div>
        <Input
          id="password"
          v-model="password"
          type="password"
          autocomplete="current-password"
          required
          :disabled="busy"
        />
      </div>

      <div v-if="needsTwoFactor" class="space-y-2">
        <Label for="totp">Two-factor code</Label>
        <Input
          id="totp"
          v-model.trim="twoFactorCode"
          inputmode="numeric"
          autocomplete="one-time-code"
          class="font-mono"
          :disabled="busy"
        />
      </div>

      <Button type="submit" class="w-full" :disabled="busy">
        {{ busy ? 'Signing in…' : 'Sign in' }}
      </Button>
    </form>

    <p class="text-muted-foreground text-center text-sm">
      Need an account?
      <RouterLink :to="{ name: 'sign-up' }" class="text-foreground font-medium hover:underline">
        Create one
      </RouterLink>
    </p>
  </div>
</template>
