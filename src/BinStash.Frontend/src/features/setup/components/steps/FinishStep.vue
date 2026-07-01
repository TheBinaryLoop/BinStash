<template>
  <div class="flex flex-col items-center justify-center py-12 gap-6">
    <div class="checkmark-wrapper">
      <svg class="checkmark-svg" viewBox="0 0 52 52" xmlns="http://www.w3.org/2000/svg">
        <circle class="checkmark-circle" cx="26" cy="26" r="24" fill="none" />
        <path class="checkmark-check" fill="none" d="M14 27 l8 8 l16 -16" />
      </svg>
    </div>

    <div class="text-center">
      <p class="mb-1 text-lg font-semibold text-ink-strong">Setup Complete!</p>
      <p class="text-sm text-ink-muted">
        Redirecting to login in
        <span class="font-bold text-success">{{ countdown }}</span>
        second{{ countdown !== 1 ? 's' : '' }}…
      </p>
    </div>

    <div v-if="error" class="mt-2 text-sm text-danger">{{ error }}</div>
  </div>
</template>

<script lang="ts" setup>
import { ref, onMounted, onUnmounted } from 'vue'
import { useRouter } from 'vue-router'
import { logoutSetup } from '@/features/setup/api/setup.api'

const router = useRouter()
const error = ref<string | null>(null)
const countdown = ref(5)

let intervalId: ReturnType<typeof setInterval> | null = null

async function onFinish() {
  try {
    await logoutSetup()
  } catch (e: any) {
    error.value = e.message || 'Failed to finish setup.'
  }
}

function goToLogin() {
  router.replace('/signin')
}

onMounted(() => {
  intervalId = setInterval(async () => {
    countdown.value--
    if (countdown.value <= 0) {
      if (intervalId !== null) {
        clearInterval(intervalId)
        intervalId = null
      }
      await onFinish()
      goToLogin()
    }
  }, 1000)
})

onUnmounted(() => {
  if (intervalId !== null) {
    clearInterval(intervalId)
  }
})
</script>

<style scoped>
.checkmark-wrapper {
  display: flex;
  align-items: center;
  justify-content: center;
}

.checkmark-svg {
  width: 96px;
  height: 96px;
}

/* Circle draw animation */
.checkmark-circle {
  stroke: var(--color-success);
  stroke-width: 3;
  stroke-dasharray: 166;
  stroke-dashoffset: 166;
  stroke-linecap: round;
  animation: stroke-circle 0.6s cubic-bezier(0.65, 0, 0.45, 1) 0.1s forwards;
}

/* Checkmark draw animation */
.checkmark-check {
  stroke: var(--color-success);
  stroke-width: 3.5;
  stroke-linecap: round;
  stroke-linejoin: round;
  stroke-dasharray: 48;
  stroke-dashoffset: 48;
  animation: stroke-check 0.4s cubic-bezier(0.65, 0, 0.45, 1) 0.7s forwards;
}

@keyframes stroke-circle {
  to {
    stroke-dashoffset: 0;
  }
}

@keyframes stroke-check {
  to {
    stroke-dashoffset: 0;
  }
}
</style>
