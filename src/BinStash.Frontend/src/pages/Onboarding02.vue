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
        <Stepper :steps="['Workspace', 'Type', 'Details', 'Done']" :current="1" />
      </div>

      <!-- Content -->
      <div class="py-8">
        <h1 class="mb-6 text-3xl font-bold text-ink-strong">Tell us about your company</h1>
        <form>
          <div class="mb-8">
            <BaseRadioGroup
              v-model="companyType"
              :columns="2"
              :options="typeOptions"
            />
          </div>
          <div class="mb-8 flex items-center justify-between gap-6 rounded-card border border-hairline bg-card px-4 py-3">
            <div>
              <div class="mb-1 text-sm font-medium text-ink-strong">💸 Lorem ipsum is place text commonly?</div>
              <div class="text-xs text-ink-muted">Lorem ipsum is placeholder text commonly used in the graphic, print, and publishing industries for previewing layouts.</div>
            </div>
            <BaseSwitch v-model="optedIn" />
          </div>
          <div class="flex items-center justify-between">
            <BaseButton variant="ghost" :to="'/onboarding-01'">&lt;- Back</BaseButton>
            <BaseButton :to="'/onboarding-03'">Next Step -&gt;</BaseButton>
          </div>
        </form>
      </div>
    </div>
  </main>
</template>

<script setup lang="ts">
import { ref } from 'vue'
import { Stepper, BaseRadioGroup, BaseSwitch, BaseButton } from '@/shared/components/ui'

const companyType = ref('individual')
const optedIn = ref(true)

const typeOptions = [
  { value: 'individual', label: 'Individual', description: 'Lorem ipsum is place text commonly used.' },
  { value: 'organization', label: 'Organization', description: 'Lorem ipsum is place text commonly used.' },
]
</script>
