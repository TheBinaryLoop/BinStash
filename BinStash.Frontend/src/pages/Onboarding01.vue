<template>
  <main class="bg-white dark:bg-gray-900 min-h-dvh">
    <div class="max-w-2xl mx-auto px-4 sm:px-6 lg:px-8 py-10">
      <div class="mb-8">
        <h1 class="text-3xl text-gray-800 dark:text-gray-100 font-bold">Complete your onboarding</h1>
        <p class="text-sm text-gray-500 dark:text-gray-400 mt-2">
          Before you can manage repositories and releases, set up your company workspace.
        </p>
      </div>

      <div v-if="error" class="mb-6">
        <div class="bg-rose-500/20 text-rose-700 dark:text-rose-200 px-3 py-2 rounded-lg">
          <span class="text-sm">{{ error }}</span>
        </div>
      </div>

      <div v-if="isLoading" class="mb-6">
        <div class="bg-slate-500/20 text-slate-700 dark:text-slate-200 px-3 py-2 rounded-lg">
          <span class="text-sm">Loading onboarding state…</span>
        </div>
      </div>

      <div v-else class="rounded-2xl border border-gray-200 dark:border-gray-700/60 bg-white dark:bg-gray-800 shadow-xs p-6 sm:p-8">
        <div v-if="hasTenants" class="space-y-4">
          <div class="text-sm text-gray-600 dark:text-gray-300">
            Your account is already linked to a company workspace. Continue to pick your tenant.
          </div>
          <button
            type="button"
            class="btn bg-violet-500 hover:bg-violet-600 text-white"
            @click="goNext"
          >
            Continue
          </button>
        </div>

        <form v-else @submit.prevent="submit" class="space-y-4">
          <div>
            <label class="block text-sm font-medium mb-1" for="tenant-name">Company name</label>
            <input
              id="tenant-name"
              class="form-input w-full"
              type="text"
              v-model.trim="tenantName"
              :disabled="busy"
              @input="onNameInput"
              required
            />
          </div>

          <div>
            <label class="block text-sm font-medium mb-1" for="tenant-slug">Company slug</label>
            <input
              id="tenant-slug"
              class="form-input w-full font-mono"
              type="text"
              v-model.trim="tenantSlug"
              :disabled="busy"
              @input="slugEdited = true"
              required
            />
            <p class="text-xs text-gray-500 dark:text-gray-400 mt-1">
              Lowercase letters, numbers, and hyphens only.
            </p>
          </div>

          <div class="pt-2">
            <button
              type="submit"
              class="btn bg-violet-500 hover:bg-violet-600 text-white"
              :disabled="busy"
            >
              <span v-if="!busy">Create company workspace</span>
              <span v-else>Creating workspace…</span>
            </button>
          </div>

          <p v-if="tenancyMode === 'Single'" class="text-xs text-gray-500 dark:text-gray-400">
            This instance is configured for single-tenant mode. This workspace will be used as your default company.
          </p>
        </form>
      </div>
    </div>
  </main>
</template>

<script setup lang="ts">
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'
import { fetchTenancyConfig, type TenancyMode } from '../api/instance'
import { createTenant, listTenantsForMember } from '../api/tenants'
import { useTenantStore } from '../stores/tenant'

const router = useRouter()
const tenantStore = useTenantStore()

const isLoading = ref(true)
const busy = ref(false)
const error = ref<string | null>(null)

const tenancyMode = ref<TenancyMode | null>(null)
const tenantName = ref('')
const tenantSlug = ref('')
const slugEdited = ref(false)

const hasTenants = computed(() => tenantStore.tenants.length > 0)

function slugify(value: string) {
  return value
    .toLowerCase()
    .replace(/[^a-z0-9]+/g, '-')
    .replace(/^-+|-+$/g, '')
}

function onNameInput() {
  if (slugEdited.value) return
  tenantSlug.value = slugify(tenantName.value)
}

async function load() {
  isLoading.value = true
  error.value = null
  try {
    try {
      const cfg = await fetchTenancyConfig()
      tenancyMode.value = cfg.mode
    } catch {
      tenancyMode.value = null
    }

    const tenants = await listTenantsForMember()
    tenantStore.setTenants(tenants)
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to load onboarding state.'
  } finally {
    isLoading.value = false
  }
}

async function goNext() {
  if (tenantStore.tenants.length === 1) {
    const id = tenantStore.tenants[0].tenantId
    tenantStore.setCurrentTenant(id)
    await router.replace(`/t/${id}`)
    return
  }
  await router.replace('/select-tenant')
}

async function submit() {
  error.value = null

  if (!tenantName.value.trim()) {
    error.value = 'Company name is required.'
    return
  }
  if (!tenantSlug.value.trim()) {
    error.value = 'Company slug is required.'
    return
  }
  if (!/^[a-z0-9-]+$/.test(tenantSlug.value.trim())) {
    error.value = 'Company slug may only contain lowercase letters, numbers, and hyphens.'
    return
  }

  busy.value = true
  try {
    await createTenant({ name: tenantName.value.trim(), slug: tenantSlug.value.trim() })
    await load()
    await goNext()
  } catch (e) {
    error.value = e instanceof Error ? e.message : 'Failed to create company workspace.'
  } finally {
    busy.value = false
  }
}

onMounted(load)
</script>