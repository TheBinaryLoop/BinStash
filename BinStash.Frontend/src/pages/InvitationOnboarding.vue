<template>
  <main class="bg-white dark:bg-gray-900 min-h-dvh">
    <div class="max-w-2xl mx-auto px-4 sm:px-6 lg:px-8 py-10">

      <!-- Logo + Theme Toggle -->
      <div class="mb-8 flex items-center justify-between">
        <router-link class="block w-8" to="/">
          <svg width="32" height="32" viewBox="0 0 32 32">
            <defs>
              <linearGradient x1="28.538%" y1="20.229%" x2="100%" y2="108.156%" id="inv-logo-a">
                <stop stop-color="#B7ACFF" stop-opacity="0" offset="0%" />
                <stop stop-color="#B7ACFF" offset="100%" />
              </linearGradient>
              <linearGradient x1="88.638%" y1="29.267%" x2="22.42%" y2="100%" id="inv-logo-b">
                <stop stop-color="#7BC8FF" stop-opacity="0" offset="0%" />
                <stop stop-color="#7BC8FF" offset="100%" />
              </linearGradient>
            </defs>
            <rect fill="#8470FF" width="32" height="32" rx="16" />
            <path
              d="M18.277.16C26.035 1.267 32 7.938 32 16c0 8.837-7.163 16-16 16a15.937 15.937 0 01-10.426-3.863L18.277.161z"
              fill="#755FF8"
            />
            <path
              d="M7.404 2.503l18.339 26.19A15.93 15.93 0 0116 32C7.163 32 0 24.837 0 16 0 10.327 2.952 5.344 7.404 2.503z"
              fill="url(#inv-logo-a)"
            />
            <path
              d="M2.223 24.14L29.777 7.86A15.926 15.926 0 0132 16c0 8.837-7.163 16-16 16-5.864 0-10.991-3.154-13.777-7.86z"
              fill="url(#inv-logo-b)"
            />
          </svg>
        </router-link>
        <ThemeToggle />
      </div>

      <div class="mb-8">
        <h1 class="text-3xl text-gray-800 dark:text-gray-100 font-bold">You've been invited!</h1>
        <p class="text-sm text-gray-500 dark:text-gray-400 mt-2">
          Review your invitation below and accept or decline it.
        </p>
      </div>

      <!-- Error -->
      <div v-if="error" class="mb-6">
        <div class="bg-rose-500/20 text-rose-700 dark:text-rose-200 px-3 py-2 rounded-lg">
          <span class="text-sm">{{ error }}</span>
        </div>
      </div>

      <!-- Declined state -->
      <div v-if="declined" class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs p-6 sm:p-8 text-center space-y-4">
        <div class="text-4xl">🚫</div>
        <p class="text-gray-700 dark:text-gray-300 text-sm">You declined the invitation.</p>
        <router-link to="/" class="btn bg-gray-100 dark:bg-gray-700 hover:bg-gray-200 dark:hover:bg-gray-600 text-gray-800 dark:text-gray-200 text-sm">
          Go to Dashboard
        </router-link>
      </div>

      <template v-else>
        <!-- Loading -->
        <div v-if="isLoading" class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs p-6 sm:p-8">
          <div class="flex items-center gap-3 text-gray-500 dark:text-gray-400 text-sm">
            <svg class="animate-spin h-4 w-4 text-violet-500" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
              <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
              <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z" />
            </svg>
            Loading invitation details…
          </div>
        </div>

        <!-- Not authenticated -->
        <div
          v-else-if="!isAuthed"
          class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs p-6 sm:p-8 space-y-6"
        >
          <!-- Invitation summary -->
          <div class="flex items-start gap-4">
            <div class="w-12 h-12 rounded-full bg-violet-100 dark:bg-violet-900/40 flex items-center justify-center shrink-0">
              <IconBuildingSkyscraper class="w-6 h-6 text-violet-500" />
            </div>
            <div>
              <p class="text-gray-800 dark:text-gray-100 font-semibold text-base">
                {{ preview?.tenantName ?? 'A workspace' }}
              </p>
              <p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
                You are invited to join as
                <span class="font-medium text-gray-700 dark:text-gray-300">{{ preview?.role ?? 'a member' }}</span>.
              </p>
              <p v-if="preview?.invitedEmail" class="text-xs text-gray-400 dark:text-gray-500 mt-1">
                Invitation sent to {{ preview.invitedEmail }}
              </p>
              <p v-if="expiresAtFormatted" class="text-xs text-gray-400 dark:text-gray-500 mt-1">
                Expires {{ expiresAtFormatted }}
              </p>
            </div>
          </div>

          <p class="text-sm text-gray-600 dark:text-gray-400">
            If you already have an account, sign in first and then choose whether to accept this invitation.
            If you create a new account, this invitation will be linked during signup automatically.
          </p>

          <div class="flex flex-col sm:flex-row gap-3">
            <router-link
              :to="{ path: '/signin', query: { redirect: currentPath, tenantId, invitationCode } }"
              class="btn bg-violet-500 hover:bg-violet-600 text-white text-sm text-center"
            >
              Sign In
            </router-link>
            <router-link
              :to="{ path: '/signup', query: { tenantId, invitationCode } }"
              class="btn bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-700 text-gray-800 dark:text-gray-200 text-sm text-center"
            >
              Create an Account
            </router-link>
          </div>
        </div>

        <!-- Authenticated: show full invitation details + actions -->
        <div
          v-else
          class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs p-6 sm:p-8 space-y-6"
        >
          <!-- Workspace header -->
          <div class="flex items-start gap-4">
            <div class="w-12 h-12 rounded-full bg-violet-100 dark:bg-violet-900/40 flex items-center justify-center shrink-0">
              <IconBuildingSkyscraper class="w-6 h-6 text-violet-500" />
            </div>
            <div class="flex-1 min-w-0">
              <p class="text-gray-800 dark:text-gray-100 font-semibold text-lg truncate">
                {{ preview?.tenantName ?? tenantId }}
              </p>
              <p v-if="preview?.tenantSlug" class="text-xs text-gray-400 dark:text-gray-500 font-mono mt-0.5">
                {{ preview.tenantSlug }}
              </p>
            </div>
          </div>

          <!-- Invitation details panel -->
          <div class="rounded-xl bg-gray-50 dark:bg-gray-700/40 border border-gray-200 dark:border-gray-700 px-4 py-3 space-y-2.5">
            <p class="text-xs font-semibold uppercase tracking-wide text-gray-500 dark:text-gray-400">Invitation details</p>

            <div class="flex items-center justify-between">
              <span class="text-sm text-gray-600 dark:text-gray-300">Your role</span>
              <span class="inline-flex items-center px-2 py-0.5 rounded-full text-xs font-medium bg-violet-100 dark:bg-violet-900/40 text-violet-700 dark:text-violet-300">
                {{ preview?.role ?? 'Member' }}
              </span>
            </div>

            <div v-if="preview?.invitedEmail" class="flex items-center justify-between">
              <span class="text-sm text-gray-600 dark:text-gray-300">Invited email</span>
              <span class="text-sm font-medium text-gray-700 dark:text-gray-200">{{ preview.invitedEmail }}</span>
            </div>

            <div v-if="expiresAtFormatted" class="flex items-center justify-between">
              <span class="text-sm text-gray-600 dark:text-gray-300">Expires</span>
              <span
                class="text-sm font-medium"
                :class="isExpiringSoon ? 'text-amber-600 dark:text-amber-400' : 'text-gray-700 dark:text-gray-200'"
              >
                {{ expiresAtFormatted }}
              </span>
            </div>
          </div>

          <!-- Actions -->
          <div class="flex flex-col sm:flex-row gap-3 pt-2">
            <button
              type="button"
              class="btn bg-violet-500 hover:bg-violet-600 text-white text-sm"
              :disabled="isAccepting"
              @click="onAccept"
            >
              <span v-if="!isAccepting">Accept Invitation</span>
              <span v-else class="flex items-center gap-2">
                <svg class="animate-spin h-4 w-4" xmlns="http://www.w3.org/2000/svg" fill="none" viewBox="0 0 24 24">
                  <circle class="opacity-25" cx="12" cy="12" r="10" stroke="currentColor" stroke-width="4" />
                  <path class="opacity-75" fill="currentColor" d="M4 12a8 8 0 018-8v8z" />
                </svg>
                Accepting…
              </span>
            </button>
            <button
              type="button"
              class="btn bg-white dark:bg-gray-800 border border-gray-300 dark:border-gray-600 hover:bg-gray-50 dark:hover:bg-gray-700 text-gray-800 dark:text-gray-200 text-sm"
              :disabled="isAccepting"
              @click="onDecline"
            >
              Decline
            </button>
          </div>
        </div>
      </template>

    </div>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { IconBuildingSkyscraper } from '@tabler/icons-vue'
import { useAuthStore } from '../stores/auth'
import { useTenantStore } from '../stores/tenant'
import ThemeToggle from '../components/ThemeToggle.vue'
import {
  acceptTenantInvitation,
  previewTenantInvitation,
  listTenantsForMember,
  type InvitationPreviewDto,
} from '../api/tenants'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const tenantStore = useTenantStore()

const tenantId = route.params.tenantId as string
const invitationCode = route.params.invitationCode as string

const isLoading = ref(true)
const isAccepting = ref(false)
const declined = ref(false)
const error = ref<string | null>(null)
const preview = ref<InvitationPreviewDto | null>(null)

const isAuthed = computed(() => !!auth.user)
const currentPath = computed(() => route.fullPath)

const expiresAtFormatted = computed(() => {
  const raw = preview.value?.expiresAt
  if (!raw) return null
  return new Date(raw).toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })
})

const isExpiringSoon = computed(() => {
  const raw = preview.value?.expiresAt
  if (!raw) return false
  const diff = new Date(raw).getTime() - Date.now()
  return diff > 0 && diff < 48 * 60 * 60 * 1000 // within 48 hours
})

async function loadPreview() {
  try {
    preview.value = await previewTenantInvitation(tenantId, invitationCode)
  } catch {
    // Preview endpoint may not exist or may require auth — degrade gracefully.
    preview.value = null
  }
}

async function load() {
  isLoading.value = true
  error.value = null
  try {
    if (!auth.user && !auth.isRestoring) {
      await auth.restore()
    }
    await loadPreview()
  } finally {
    isLoading.value = false
  }
}

async function onAccept() {
  error.value = null
  isAccepting.value = true
  try {
    await acceptTenantInvitation(tenantId, invitationCode)

    const tenants = await listTenantsForMember()
    tenantStore.setTenants(tenants)
    tenantStore.setCurrentTenant(tenantId)

    await router.replace(`/t/${tenantId}`)
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to accept the invitation. It may have expired or already been used.'
  } finally {
    isAccepting.value = false
  }
}

function onDecline() {
  declined.value = true
}

onMounted(load)
</script>