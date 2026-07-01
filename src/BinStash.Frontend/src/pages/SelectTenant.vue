<template>
  <main class="bg-canvas text-ink-strong min-h-dvh">
    <div class="max-w-2xl mx-auto px-4 sm:px-6 lg:px-8 py-10">
      <div class="mb-8">
        <h1 class="text-3xl font-bold tracking-tight text-ink-strong">Select a tenant</h1>
        <p class="text-sm text-ink-muted mt-2">
          Choose where you want to work. You can switch later from the header.
        </p>
      </div>

      <div v-if="error" class="mb-6">
        <div class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
          {{ error }}
        </div>
      </div>

      <div v-if="isLoading" class="mb-6 flex items-center gap-3 text-sm text-ink-muted">
        <Spinner :size="20" :thickness="2" color="var(--color-accent)" />
        Loading tenants…
      </div>

      <EmptyState
        v-if="!isLoading && tenants.length === 0"
        :icon="IconBuildingSkyscraper"
        title="No tenants"
        description="Your account is not a member of any tenant yet."
      />

      <div v-else class="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <button
          v-for="t in tenants"
          :key="t.tenantId"
          type="button"
          class="text-left rounded-card border border-hairline bg-card p-5 shadow-sm transition hover:border-accent hover:bg-raised"
          @click="select(t.tenantId)"
        >
          <div class="flex items-center justify-between">
            <div class="font-semibold text-ink-strong">{{ t.name }}</div>
            <div class="text-xs text-ink-subtle">{{ shortId(t.tenantId) }}</div>
          </div>
          <div v-if="t.slug" class="text-sm text-ink-muted mt-1">
            {{ t.slug }}
          </div>
        </button>
      </div>

      <div class="mt-8">
        <BaseButton variant="ghost" size="sm" @click="logout()">
          Sign in with a different account
        </BaseButton>
      </div>
    </div>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { IconBuildingSkyscraper } from '@tabler/icons-vue'
import { listTenantsForMember } from '../api/tenants'
import { useTenantStore } from '../stores/tenant'
import { useAuthStore } from '../stores/auth'
import Spinner from '@/shared/components/feedback/Spinner.vue'
import { BaseButton, EmptyState } from '@/shared/components/ui'

const auth = useAuthStore()
const tenantStore = useTenantStore()
const router = useRouter()
const route = useRoute()

const isLoading = ref(false)
const error = ref<string | null>(null)

const tenants = computed(() => tenantStore.tenants)

function shortId(id: string) {
    if (!id) return '';
    return id.length > 8 ? id.slice(0, 8) : id
}

function redirectTargetFor(tenantId: string) {
  const redirect = typeof route.query.redirect === 'string' ? route.query.redirect : ''
  // If redirect is a tenant route already, prefer it; otherwise go tenant home
  if (redirect.startsWith('/t/')) return redirect
  return `/t/${tenantId}`
}

async function select(tenantId: string) {
  tenantStore.setCurrentTenant(tenantId)
  await router.replace(redirectTargetFor(tenantId))
}

async function logout() {
  await auth.logout()
  router.replace('/signin')
}

onMounted(async () => {
  // load if needed
  if (!tenantStore.isLoaded) {
    isLoading.value = true
    error.value = null
    try {
      const data = await listTenantsForMember()
      tenantStore.setTenants(data)
    } catch (e) {
      error.value = e instanceof Error ? e.message : 'Failed to load tenants.'
    } finally {
      isLoading.value = false
    }
  }

  // convenience: auto-pick if only one
  if (tenantStore.tenants.length === 1) {
    await select(tenantStore.tenants[0].tenantId)
  }
})
</script>
