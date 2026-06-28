<template>
  <form @submit.prevent="onSubmit" class="flex flex-col gap-4">
    <h2 class="text-xl font-bold text-gray-800 dark:text-gray-100">Step 5: Create {{ adminLabel }}</h2>
    <p class="text-gray-600 dark:text-gray-400">
      Set up the first {{ adminLabel.toLowerCase() }} account for BinStash.
    </p>
    <div class="flex flex-col gap-1">
      <label for="admin-email" class="text-sm font-medium text-gray-700 dark:text-gray-300">Email</label>
      <input
        id="admin-email"
        v-model="email"
        type="email"
        required
        autocomplete="username"
        :disabled="loading"
        class="w-full px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500 disabled:opacity-50"
      />
    </div>
    <div class="flex flex-col gap-1">
      <label for="admin-password" class="text-sm font-medium text-gray-700 dark:text-gray-300">Password</label>
      <div class="flex items-center gap-2">
        <input
          id="admin-password"
          v-model="password"
          :type="showPassword ? 'text' : 'password'"
          required
          autocomplete="new-password"
          :disabled="loading"
          class="flex-1 px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500 disabled:opacity-50"
        />
      </div>
      <div
        class="text-sm mt-1"
        :class="passwordStrength < 2 ? 'text-red-600 dark:text-red-400' : 'text-green-600 dark:text-green-400'"
      >
        Password strength: {{ passwordStrengthText }}
      </div>
    </div>
    <div class="flex flex-col gap-1">
      <label for="admin-firstname" class="text-sm font-medium text-gray-700 dark:text-gray-300">First Name</label>
      <input
        id="admin-firstname"
        v-model="firstName"
        type="text"
        autocomplete="given-name"
        :disabled="loading"
        class="w-full px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500 disabled:opacity-50"
      />
    </div>
    <div class="flex flex-col gap-1">
      <label for="admin-lastname" class="text-sm font-medium text-gray-700 dark:text-gray-300">Last Name</label>
      <input
        id="admin-lastname"
        v-model="lastName"
        type="text"
        autocomplete="family-name"
        :disabled="loading"
        class="w-full px-3 py-2 text-sm rounded-md border border-gray-300 dark:border-gray-600 bg-white dark:bg-gray-700/50 text-gray-800 dark:text-gray-100 placeholder-gray-400 dark:placeholder-gray-500 focus:outline-none focus:ring-2 focus:ring-violet-500 disabled:opacity-50"
      />
    </div>
    <div v-if="success" class="text-green-600 dark:text-green-400 text-sm">
      Admin user created successfully.
    </div>
    <div v-if="error" class="text-red-600 dark:text-red-400 text-sm">{{ error }}</div>
    <button
      type="submit"
      :disabled="loading || !email || !password"
      class="flex items-center justify-center px-6 py-2 text-sm font-medium bg-violet-500 hover:bg-violet-600 text-white rounded-md cursor-pointer disabled:opacity-50 disabled:cursor-not-allowed transition-colors"
    >
      <Spinner v-if="loading" color="white" class="w-4 h-4 mr-2" />
      {{ loading ? 'Creating...' : `Create ${adminLabel.toLowerCase()}` }}
    </button>
  </form>
</template>

<script lang="ts" setup>
import { ref, computed } from 'vue'
import { useSetupStore } from '@/features/setup/store/setup.store'
import { createAdminUser } from '@/features/setup/api/setup.api'
import Spinner from '@/shared/components/feedback/Spinner.vue'

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