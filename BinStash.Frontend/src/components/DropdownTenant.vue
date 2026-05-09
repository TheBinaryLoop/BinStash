<template>
  <div class="relative inline-flex">
    <!-- Trigger -->
    <button
      ref="trigger"
      class="w-8 h-8 flex items-center justify-center hover:bg-gray-100 lg:hover:bg-gray-200 dark:hover:bg-gray-700/50 dark:lg:hover:bg-gray-800 rounded-full"
      :class="{ 'bg-gray-200 dark:bg-gray-800': open }"
      @click.stop="toggle"
      aria-haspopup="true"
      :aria-expanded="open"
      :title="currentTitle"
    >
      <span class="sr-only">Switch tenant</span>

      <!-- Simple "building" icon -->
      <svg class="fill-current text-gray-500/80 dark:text-gray-400/80" width="16" height="16" viewBox="0 0 16 16">
        <path
          d="M3 14V2a1 1 0 0 1 1-1h8a1 1 0 0 1 1 1v12h-2v-2H5v2H3zm2-4h2V8H5v2zm0-3h2V5H5v2zm4 3h2V8H9v2zm0-3h2V5H9v2z"
        />
      </svg>
    </button>

    <!-- Dropdown -->
    <transition
      enter-active-class="transition ease-out duration-200 transform"
      enter-from-class="opacity-0 -translate-y-2"
      enter-to-class="opacity-100 translate-y-0"
      leave-active-class="transition ease-out duration-200"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <div
        v-if="open"
        ref="dropdown"
        class="origin-top-right z-50 absolute top-full mt-2 min-w-72 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700/60 rounded-lg shadow-lg overflow-hidden"
        :class="align === 'right' ? 'right-0' : 'left-0'"
      >
        <div class="px-4 py-3 border-b border-gray-200 dark:border-gray-700/60">
          <div class="text-xs text-gray-500 dark:text-gray-400">Current tenant</div>
          <div class="mt-1 font-semibold text-gray-800 dark:text-gray-100 truncate">
            {{ currentTenantLabel }}
          </div>
        </div>

        <div class="max-h-72 overflow-y-auto">
          <div v-if="tenant.isLoading" class="px-4 py-3 text-sm text-gray-600 dark:text-gray-300">
            Loading tenants…
          </div>

          <template v-else>
            <button
              v-for="t in tenant.tenants"
              :key="t.tenantId"
              type="button"
              class="w-full text-left px-4 py-3 hover:bg-gray-50 dark:hover:bg-gray-900/20"
              @click="switchTo(t.tenantId)"
            >
              <div class="flex items-start justify-between gap-3">
                <div class="min-w-0">
                  <div class="font-medium text-gray-800 dark:text-gray-100 truncate">
                    {{ t.name }}
                  </div>
                  <div v-if="t.slug" class="text-xs text-gray-500 dark:text-gray-400 truncate">
                    {{ t.slug }}
                  </div>
                </div>

                <div v-if="t.tenantId === tenant.currentTenantId" class="text-violet-500 shrink-0">
                  <svg class="fill-current" width="16" height="16" viewBox="0 0 16 16">
                    <path d="M6.5 12.3 2.8 8.6l1.4-1.4 2.3 2.3 5.2-5.2 1.4 1.4z" />
                  </svg>
                </div>
              </div>
            </button>

            <div v-if="tenant.tenants.length === 0" class="px-4 py-3 text-sm text-gray-600 dark:text-gray-300">
              No tenants found.
            </div>
          </template>
        </div>

        <div class="px-4 py-2 border-t border-gray-200 dark:border-gray-700/60">
          <button
            type="button"
            class="text-sm text-gray-600 dark:text-gray-300 hover:underline"
            @click="refresh"
          >
            Refresh
          </button>
        </div>
      </div>
    </transition>
  </div>
</template>

<script setup lang="ts">
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'
import { useTenantStore } from '../stores/tenant'
import { listTenantsForMember } from '../api/tenants'

const props = defineProps<{
  align?: 'left' | 'right'
  tenantHomeMode?: 'root' | 'repositories' // optional, default root
}>()

const align = computed(() => props.align ?? 'right')
const tenantHomeMode = computed(() => props.tenantHomeMode ?? 'root')

const tenant = useTenantStore()
const router = useRouter()
const route = useRoute()

const open = ref(false)
const trigger = ref<HTMLElement | null>(null)
const dropdown = ref<HTMLElement | null>(null)

const currentTenantLabel = computed(() => {
  const t = tenant.currentTenant
  if (!t) return 'None selected'
  return t.slug ? `${t.name} (${t.slug})` : t.name
})

const currentTitle = computed(() => `Tenant: ${currentTenantLabel.value}`)

function tenantHome(id: string) {
  return tenantHomeMode.value === 'repositories'
    ? `/t/${id}/repositories`
    : `/t/${id}`
}

// Hybrid rule:
// - If current route is tenant-scoped => keep subpath, replace tenantId
// - else => go tenant home
function buildTargetForTenant(newTenantId: string) {
  const full = route.fullPath // includes query/hash
  if (full.startsWith('/t/')) {
    // Replace the first "/t/{something}" segment
    return full.replace(/^\/t\/[^/]+/, `/t/${newTenantId}`)
  }
  return tenantHome(newTenantId)
}

async function ensureLoaded() {
  if (tenant.isLoaded || tenant.isLoading) return
  tenant.isLoading = true
  try {
    tenant.setTenants(await listTenantsForMember())
  } finally {
    tenant.isLoading = false
  }
}

async function refresh() {
  tenant.isLoading = true
  try {
    tenant.setTenants(await listTenantsForMember())
  } finally {
    tenant.isLoading = false
  }
}

async function switchTo(newTenantId: string) {
  if (!newTenantId || newTenantId === tenant.currentTenantId) {
    open.value = false
    return
  }

  // validate membership quickly (optional, but nice)
  const exists = tenant.tenants.some(t => t.tenantId === newTenantId)
  if (!exists) return

  tenant.setCurrentTenant(newTenantId)

  const target = buildTargetForTenant(newTenantId)
  open.value = false
  await router.push(target)
}

async function toggle() {
  open.value = !open.value
  if (open.value) await ensureLoaded()
}

function onDocumentClick(e: MouseEvent) {
  if (!open.value) return
  const el = e.target as Node
  if (trigger.value?.contains(el)) return
  if (dropdown.value?.contains(el)) return
  open.value = false
}

function onKeydown(e: KeyboardEvent) {
  if (!open.value) return
  if (e.key === 'Escape') open.value = false
}

onMounted(() => {
  document.addEventListener('click', onDocumentClick)
  document.addEventListener('keydown', onKeydown)
})

onBeforeUnmount(() => {
  document.removeEventListener('click', onDocumentClick)
  document.removeEventListener('keydown', onKeydown)
})
</script>
