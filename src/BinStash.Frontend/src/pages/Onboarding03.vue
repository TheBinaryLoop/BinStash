<template>
  <main class="min-h-dvh bg-canvas">
    <div class="mx-auto flex min-h-dvh max-w-2xl flex-col px-4 sm:px-6 lg:px-8">
      <!-- Header -->
      <div class="flex h-16 items-center justify-between">
        <router-link class="flex items-center gap-2" to="/">
          <span class="flex h-8 w-8 items-center justify-center rounded-control bg-linear-to-br from-brand-from to-brand-to" aria-hidden="true" />
          <span class="text-sm font-semibold text-ink-strong">BinStash</span>
        </router-link>
        <div class="text-sm text-ink-muted">
          Have an account?
          <router-link class="font-medium text-accent transition hover:brightness-110" to="/signin">Sign In</router-link>
        </div>
      </div>

      <!-- Progress bar -->
      <div class="pt-12 pb-8">
        <Stepper :steps="['Workspace', 'Type', 'Details', 'Done']" :current="2" />
      </div>

      <!-- Content -->
      <div class="py-8">
        <h1 class="mb-6 text-3xl font-bold text-ink-strong">Company information</h1>
        <form>
          <div class="mb-8 space-y-4">
            <BaseInput v-model="companyName" label="Company Name" type="text" required />
            <div class="flex gap-4">
              <BaseInput v-model="city" label="City" type="text" required class="flex-1" />
              <BaseInput v-model="postalCode" label="Postal Code" type="text" required class="flex-1" />
            </div>
            <BaseInput v-model="street" label="Street Address" type="text" required />
            <BaseSelect
              v-model="country"
              label="Country"
              :options="['USA', 'Italy', 'United Kingdom']"
            />
          </div>
          <div class="flex items-center justify-between">
            <BaseButton variant="ghost" :to="'/onboarding-02'">&lt;- Back</BaseButton>
            <BaseButton :to="'/onboarding-04'">Next Step -&gt;</BaseButton>
          </div>
        </form>
      </div>
    </div>
  </main>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { Stepper, FormField, BaseInput, BaseSelect, BaseButton } from '@/shared/components/ui'

const companyName = ref('')
const city = ref('')
const postalCode = ref('')
const street = ref('')
const country = ref('USA')
</script>
