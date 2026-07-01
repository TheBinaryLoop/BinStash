<template>
  <main class="min-h-dvh bg-canvas">
    <div class="mx-auto max-w-2xl px-4 py-10 sm:px-6 lg:px-8">
      <div class="mb-8">
        <Stepper :steps="['Workspace', 'Type', 'Details', 'Done']" :current="0" />
      </div>

      <div class="mb-8">
        <h1 class="text-3xl font-bold text-ink-strong">Complete your onboarding</h1>
        <p class="mt-2 text-sm text-ink-muted">
          Before you can manage repositories and releases, set up your company workspace.
        </p>
      </div>

      <div v-if="error" class="mb-6">
        <div class="rounded-card border border-danger/20 bg-danger-soft px-4 py-3 text-sm text-danger">
          {{ error }}
        </div>
      </div>

      <div v-if="isLoading" class="flex items-center justify-center py-12">
        <Spinner :size="24" color="var(--color-accent)" />
      </div>

      <div v-else class="rounded-card border border-hairline bg-card p-6 shadow-sm sm:p-8">
        <div v-if="hasTenants" class="space-y-4">
          <div class="text-sm text-ink-muted">
            Your account is already linked to a company workspace. Continue to pick your tenant.
          </div>
          <BaseButton @click="goNext">Continue</BaseButton>
        </div>

        <form v-else @submit.prevent="submit" class="space-y-4">
          <FormField label="Company name" for-id="tenant-name">
            <BaseInput
              id="tenant-name"
              v-model.trim="tenantName"
              type="text"
              :disabled="busy"
              required
              @input="onNameInput"
            />
          </FormField>

          <FormField
            label="Company slug"
            for-id="tenant-slug"
            hint="Lowercase letters, numbers, and hyphens only."
          >
            <BaseInput
              id="tenant-slug"
              v-model.trim="tenantSlug"
              type="text"
              class="font-mono"
              :disabled="busy"
              required
              @input="slugEdited = true"
            />
          </FormField>

          <div class="pt-2">
            <BaseButton type="submit" :loading="busy" :disabled="busy">
              {{ busy ? 'Creating workspace…' : 'Create company workspace' }}
            </BaseButton>
          </div>

          <p v-if="tenancyMode === 'Single'" class="text-xs text-ink-subtle">
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
import { Stepper, FormField, BaseInput, BaseButton } from '@/shared/components/ui'
import Spinner from '@/shared/components/feedback/Spinner.vue'

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
