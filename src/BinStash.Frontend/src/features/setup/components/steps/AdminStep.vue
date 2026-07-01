<template>
  <form @submit.prevent="onSubmit" class="flex flex-col gap-4">
    <h2 class="text-xl font-bold text-ink-strong">Step 5: Create {{ adminLabel }}</h2>
    <p class="text-sm text-ink-muted">
      Set up the first {{ adminLabel.toLowerCase() }} account for BinStash.
    </p>
    <BaseInput
      v-model="email"
      label="Email"
      type="email"
      required
      autocomplete="username"
      :disabled="loading"
    />
    <BaseInput
      v-model="password"
      label="Password"
      :type="showPassword ? 'text' : 'password'"
      required
      autocomplete="new-password"
      :disabled="loading"
    />
    <div
      class="-mt-2 text-sm"
      :class="passwordStrength < 2 ? 'text-danger' : 'text-success'"
    >
      Password strength: {{ passwordStrengthText }}
    </div>
    <BaseInput
      v-model="firstName"
      label="First Name"
      type="text"
      autocomplete="given-name"
      :disabled="loading"
    />
    <BaseInput
      v-model="lastName"
      label="Last Name"
      type="text"
      autocomplete="family-name"
      :disabled="loading"
    />
    <div
      v-if="success"
      class="rounded-card border border-success/25 bg-success-soft px-4 py-3 text-sm text-success"
    >
      Admin user created successfully.
    </div>
    <div
      v-if="error"
      class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger"
    >{{ error }}</div>
    <BaseButton
      type="submit"
      :loading="loading"
      :disabled="loading || !email || !password"
    >
      {{ loading ? 'Creating...' : `Create ${adminLabel.toLowerCase()}` }}
    </BaseButton>
  </form>
</template>

<script lang="ts" setup>
import { ref, computed } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { createAdminUser } from '@/features/setup/api/setup.api'
import { BaseInput, BaseButton } from '@/shared/components/ui'

const setupStore = useSetupStore()
const email = ref('')
const password = ref('')
const firstName = ref('')
const lastName = ref('')
const showPassword = ref(false)
const loading = ref(false)
const error = ref<string | null>(null)
const success = ref(false)

const adminLabel = computed(() =>
  setupStore.currentStep === 'InstanceAdmin' ? 'Instance admin' : 'Tenant admin'
)

const passwordStrength = computed(() => {
  let score = 0
  if (password.value.length >= 8) score++
  if (/[A-Z]/.test(password.value)) score++
  if (/[0-9]/.test(password.value)) score++
  if (/[^A-Za-z0-9]/.test(password.value)) score++
  return score
})
const passwordStrengthText = computed(() => {
  if (!password.value) return 'Empty'
  if (passwordStrength.value >= 3) return 'Strong'
  if (passwordStrength.value === 2) return 'Medium'
  return 'Weak'
})

async function onSubmit() {
  loading.value = true
  error.value = null
  success.value = false
  try {
    await createAdminUser({
      isTenantAdmin: setupStore.currentStep === 'TenantAdmin',
      isInstanceAdmin: setupStore.currentStep === 'InstanceAdmin',
      email: email.value.trim(),
      password: password.value,
      firstName: firstName.value.trim() || undefined,
      lastName: lastName.value.trim() || undefined,
    })
    success.value = true
    password.value = ''
    await setupStore.fetchStatus()
  } catch (e: any) {
    if (e.message && e.message.includes('409')) {
      error.value = 'Admin user already exists.'
    } else {
      error.value = e.message || 'Failed to create admin user.'
    }
    password.value = ''
  } finally {
    loading.value = false
  }
}
</script>
