<template>
  <main class="bg-canvas text-ink-strong min-h-dvh">
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
        <h1 class="text-3xl font-bold tracking-tight text-ink-strong">You've been invited!</h1>
        <p class="text-sm text-ink-muted mt-2">
          Review your invitation below and accept or decline it.
        </p>
      </div>

      <!-- Error -->
      <div v-if="error" class="mb-6">
        <div class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
          {{ error }}
        </div>
      </div>

      <!-- Declined state -->
      <div v-if="declined" class="rounded-card border border-hairline bg-card shadow-xs p-6 sm:p-8 text-center space-y-4">
        <div class="text-4xl">🚫</div>
        <p class="text-ink-muted text-sm">You declined the invitation.</p>
        <BaseButton variant="secondary" to="/">Go to Dashboard</BaseButton>
      </div>

      <template v-else>
        <!-- Loading -->
        <div v-if="isLoading" class="rounded-card border border-hairline bg-card shadow-xs p-6 sm:p-8">
          <div class="flex items-center gap-3 text-ink-muted text-sm">
            <Spinner :size="16" :thickness="2" color="var(--color-accent)" />
            Loading invitation details…
          </div>
        </div>

        <!-- Not authenticated -->
        <div
          v-else-if="!isAuthed"
          class="rounded-card border border-hairline bg-card shadow-xs p-6 sm:p-8 space-y-6"
        >
          <!-- Invitation summary -->
          <div class="flex items-start gap-4">
            <div class="flex h-12 w-12 items-center justify-center rounded-full bg-accent-soft text-accent shrink-0">
              <IconBuildingSkyscraper class="w-6 h-6" />
            </div>
            <div>
              <p class="text-ink-strong font-semibold text-base">
                {{ preview?.tenantName ?? 'A workspace' }}
              </p>
              <p class="text-sm text-ink-muted mt-0.5">
                You are invited to join as
                <span class="font-medium text-ink-strong">{{ preview?.role ?? 'a member' }}</span>.
              </p>
              <p v-if="preview?.invitedEmail" class="text-xs text-ink-subtle mt-1">
                Invitation sent to {{ preview.invitedEmail }}
              </p>
              <p v-if="expiresAtFormatted" class="text-xs text-ink-subtle mt-1">
                Expires {{ expiresAtFormatted }}
              </p>
            </div>
          </div>

          <p class="text-sm text-ink-muted">
            If you already have an account, sign in first and then choose whether to accept this invitation.
            If you create a new account, this invitation will be linked during signup automatically.
          </p>

          <div class="flex flex-col sm:flex-row gap-3">
            <BaseButton :to="{ path: '/signin', query: { redirect: currentPath, tenantId, invitationCode } }">
              Sign In
            </BaseButton>
            <BaseButton variant="secondary" :to="{ path: '/signup', query: { tenantId, invitationCode } }">
              Create an Account
            </BaseButton>
          </div>
        </div>

        <!-- Authenticated: show full invitation details + actions -->
        <div
          v-else
          class="rounded-card border border-hairline bg-card shadow-xs p-6 sm:p-8 space-y-6"
        >
          <!-- Workspace header -->
          <div class="flex items-start gap-4">
            <div class="flex h-12 w-12 items-center justify-center rounded-full bg-accent-soft text-accent shrink-0">
              <IconBuildingSkyscraper class="w-6 h-6" />
            </div>
            <div class="flex-1 min-w-0">
              <p class="text-ink-strong font-semibold text-lg truncate">
                {{ preview?.tenantName ?? tenantId }}
              </p>
              <p v-if="preview?.tenantSlug" class="text-xs text-ink-subtle font-mono mt-0.5">
                {{ preview.tenantSlug }}
              </p>
            </div>
          </div>

          <!-- Invitation details panel -->
          <div class="rounded-control border border-hairline bg-raised px-4 py-3 space-y-2.5">
            <p class="text-xs font-semibold uppercase tracking-wide text-ink-subtle">Invitation details</p>

            <div class="flex items-center justify-between">
              <span class="text-sm text-ink-muted">Your role</span>
              <BaseBadge tone="accent">{{ preview?.role ?? 'Member' }}</BaseBadge>
            </div>

            <div v-if="preview?.invitedEmail" class="flex items-center justify-between">
              <span class="text-sm text-ink-muted">Invited email</span>
              <span class="text-sm font-medium text-ink-strong">{{ preview.invitedEmail }}</span>
            </div>

            <div v-if="expiresAtFormatted" class="flex items-center justify-between">
              <span class="text-sm text-ink-muted">Expires</span>
              <span
                class="text-sm font-medium"
                :class="isExpiringSoon ? 'text-warning' : 'text-ink-strong'"
              >
                {{ expiresAtFormatted }}
              </span>
            </div>
          </div>

          <!-- Actions -->
          <div class="flex flex-col sm:flex-row gap-3 pt-2">
            <BaseButton :loading="isAccepting" :disabled="isAccepting" @click="onAccept">
              {{ isAccepting ? 'Accepting…' : 'Accept Invitation' }}
            </BaseButton>
            <BaseButton variant="secondary" :disabled="isAccepting" @click="onDecline">
              Decline
            </BaseButton>
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
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BaseButton, BaseBadge } from '@/shared/components/ui'
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