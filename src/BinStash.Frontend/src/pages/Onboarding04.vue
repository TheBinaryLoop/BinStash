<template>
  <main class="min-h-dvh bg-canvas text-ink-strong">
    <div class="relative flex">
      <!-- Content -->
      <div class="w-full md:w-1/2">
        <div class="flex min-h-dvh flex-col after:flex-1">
          <div class="flex-1">
            <!-- Header -->
            <div class="flex h-16 items-center justify-between px-4 sm:px-6 lg:px-8">
              <router-link class="flex items-center gap-2.5" to="/">
                <span class="flex h-8 w-8 items-center justify-center rounded-[10px] bg-linear-to-br from-brand-from to-brand-to shadow-sm">
                  <svg class="h-5 w-5 fill-white" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
                    <path d="M31.956 14.8C31.372 6.92 25.08.628 17.2.044V5.76a9.04 9.04 0 0 0 9.04 9.04h5.716ZM14.8 26.24v5.716C6.92 31.372.63 25.08.044 17.2H5.76a9.04 9.04 0 0 1 9.04 9.04Zm11.44-9.04h5.716c-.584 7.88-6.876 14.172-14.756 14.756V26.24a9.04 9.04 0 0 1 9.04-9.04ZM.044 14.8C.63 6.92 6.92.628 14.8.044V5.76a9.04 9.04 0 0 1-9.04 9.04H.044Z" />
                  </svg>
                </span>
                <span class="text-sm font-semibold tracking-tight text-ink-strong">BinStash</span>
              </router-link>
              <div class="text-sm text-ink-muted">
                Have an account?
                <router-link class="font-medium text-accent transition hover:brightness-110" to="/signin">Sign In</router-link>
              </div>
            </div>

            <!-- Progress bar -->
            <div class="px-4 pt-12 pb-8">
              <div class="mx-auto w-full max-w-md">
                <Stepper :steps="steps" :current="3" />
              </div>
            </div>
          </div>

          <div class="px-4 py-8">
            <div class="mx-auto max-w-md text-center">
              <span class="mx-auto mb-6 inline-flex h-16 w-16 items-center justify-center rounded-full bg-success-soft text-success">
                <IconCircleCheck class="h-9 w-9" />
              </span>
              <h1 class="mb-8 text-2xl font-bold tracking-tight text-ink-strong sm:text-3xl">You're all set! 🎉</h1>
              <BaseButton to="/" size="lg">Go to dashboard</BaseButton>
            </div>
          </div>
        </div>
      </div>

      <!-- Image -->
      <div class="absolute bottom-0 right-0 top-0 hidden md:block md:w-1/2" aria-hidden="true">
        <img class="h-full w-full object-cover object-center" src="../images/onboarding-image.jpg" width="760" height="1024" alt="Onboarding" />
      </div>
    </div>
  </main>
</template>

<script setup lang="ts">
import { IconCircleCheck } from '@tabler/icons-vue'
import { Stepper, BaseButton } from '@/shared/components/ui'

const steps = ['Company', 'Preferences', 'Details', 'Done']
</script>
