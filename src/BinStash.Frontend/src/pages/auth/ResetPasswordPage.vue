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
const route = useRoute()
const router = useRouter()

const email = ref(typeof route.query.email === 'string' ? route.query.email : '')
const code = ref(typeof route.query.code === 'string' ? route.query.code : '')
const password = ref('')
const confirmation = ref('')
const error = ref('')
const busy = ref(false)

async function submit() {
  if (password.value !== confirmation.value) {
    error.value = 'The two passwords do not match.'
    return
  }

  busy.value = true
  error.value = ''

  try {
    await auth.resetPassword(email.value, code.value, password.value)
    await router.push({ name: 'sign-in', query: { reset: '1' } })
  } catch (caught) {
    error.value = errorMessage(caught, 'Could not reset your password. The link may have expired.')
  } finally {
    busy.value = false
  }
}
</script>

<template>
  <div class="space-y-6">
    <div class="space-y-1.5">
      <h1 class="text-xl font-semibold tracking-tight">Choose a new password</h1>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <form class="space-y-4" @submit.prevent="submit">
      <div class="space-y-2">
        <Label for="email">Email</Label>
        <Input id="email" v-model.trim="email" type="email" required :disabled="busy" />
      </div>

      <div v-if="!route.query.code" class="space-y-2">
        <Label for="code">Reset code</Label>
        <Input id="code" v-model.trim="code" class="font-mono" required :disabled="busy" />
      </div>

      <div class="space-y-2">
        <Label for="password">New password</Label>
        <Input
          id="password"
          v-model="password"
          type="password"
          autocomplete="new-password"
          required
          :disabled="busy"
        />
      </div>

      <div class="space-y-2">
        <Label for="confirm">Confirm new password</Label>
        <Input
          id="confirm"
          v-model="confirmation"
          type="password"
          autocomplete="new-password"
          required
          :disabled="busy"
        />
      </div>

      <Button type="submit" class="w-full" :disabled="busy">
        {{ busy ? 'Saving…' : 'Reset password' }}
      </Button>
    </form>
  </div>
</template>
