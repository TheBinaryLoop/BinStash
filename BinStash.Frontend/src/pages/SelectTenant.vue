<template>
  <main class="bg-white dark:bg-gray-900 min-h-dvh">
    <div class="max-w-2xl mx-auto px-4 sm:px-6 lg:px-8 py-10">
      <div class="mb-8">
        <h1 class="text-3xl text-gray-800 dark:text-gray-100 font-bold">Select a tenant</h1>
        <p class="text-sm text-gray-500 dark:text-gray-400 mt-2">
          Choose where you want to work. You can switch later from the header.
        </p>
      </div>

      <div v-if="error" class="mb-6">
        <div class="bg-rose-500/20 text-rose-700 dark:text-rose-200 px-3 py-2 rounded-lg">
          <span class="text-sm">{{ error }}</span>
        </div>
      </div>

      <div v-if="isLoading" class="mb-6">
        <div class="bg-slate-500/20 text-slate-700 dark:text-slate-200 px-3 py-2 rounded-lg">
          <span class="text-sm">Loading tenants…</span>
        </div>
      </div>

      <div v-if="!isLoading && tenants.length === 0" class="bg-white dark:bg-gray-800 shadow-sm rounded-xl p-6 border border-gray-200 dark:border-gray-700/60">
        <div class="text-gray-800 dark:text-gray-100 font-semibold mb-1">No tenants</div>
        <div class="text-sm text-gray-500 dark:text-gray-400">
          Your account is not a member of any tenant yet.
        </div>
      </div>

      <div v-else class="grid grid-cols-1 sm:grid-cols-2 gap-4">
        <button
          v-for="t in tenants"
          :key="t.tenantId"
          type="button"
          class="text-left bg-white dark:bg-gray-800 shadow-sm rounded-xl p-5 border border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 transition"
          @click="select(t.tenantId)"
        >
          <div class="flex items-center justify-between">
            <div class="font-semibold text-gray-800 dark:text-gray-100">{{ t.name }}</div>
            <div class="text-xs text-gray-400 dark:text-gray-500">{{ shortId(t.tenantId) }}</div>
          </div>
          <div v-if="t.slug" class="text-sm text-gray-500 dark:text-gray-400 mt-1">
            {{ t.slug }}
          </div>
        </button>
      </div>

      <div class="mt-8">
        <p class="text-sm underline hover:no-underline hover:cursor-pointer text-gray-600 dark:text-gray-300" @click="logout()">
          Sign in with a different account
        </p>
      </div>
    </div>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { listTenantsForMember } from '../api/tenants'
import { useTenantStore } from '../stores/tenant'
import { useAuthStore } from '../stores/auth'

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
