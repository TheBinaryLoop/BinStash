<template>
  <div class="space-y-6">

    <!-- ── Detail view ──────────────────────────────────────────────────── -->
    <template v-if="selectedSection">
      <button
        @click="goBack"
        class="flex items-center gap-1.5 text-sm text-gray-500 dark:text-gray-400 hover:text-violet-600 dark:hover:text-violet-400 transition font-medium"
      >
        <IconArrowLeft class="w-4 h-4" />
        Back to Tenant Settings
      </button>

      <TenantProfileSettings v-if="selectedSection === 'profile'" />
      <TenantStorageSettings v-else-if="selectedSection === 'storage'" />
      <TenantSSOSettings v-else-if="selectedSection === 'sso'" />
      <TenantMembershipSettings v-else-if="selectedSection === 'membership'" />

      <div
        v-else
        class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-10 text-center border border-dashed border-gray-300 dark:border-gray-600"
      >
        <div class="w-12 h-12 rounded-full bg-gray-100 dark:bg-gray-700 flex items-center justify-center mx-auto mb-3">
          <component :is="currentSectionIcon" class="text-gray-400 w-6 h-6" />
        </div>
        <p class="text-gray-600 dark:text-gray-400 font-medium">
          {{ currentSectionLabel }} — Coming Soon
        </p>
        <p class="text-sm text-gray-500 dark:text-gray-500 mt-1 max-w-sm mx-auto">
          {{ currentSectionDescription }}
        </p>
      </div>
    </template>

    <!-- ── List view ────────────────────────────────────────────────────── -->
    <template v-else>
      <div>
        <h2 class="text-lg font-semibold text-gray-800 dark:text-gray-100">General Settings</h2>
        <p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
          Configure tenant profile, storage visibility, and member self-service controls.
        </p>
      </div>

      <div class="grid grid-cols-1 md:grid-cols-2 gap-4">
        <button
          v-for="section in SECTIONS"
          :key="section.id"
          @click="section.available ? openSection(section.id) : undefined"
          :disabled="!section.available"
          class="group text-left bg-white dark:bg-gray-800 rounded-xl p-5 border transition"
          :class="section.available
            ? 'border-gray-200 dark:border-gray-700 hover:border-violet-300 dark:hover:border-violet-500/50 hover:shadow-sm cursor-pointer'
            : 'border-dashed border-gray-300 dark:border-gray-600 opacity-60 cursor-default'"
        >
          <div class="flex items-start gap-3">
            <div
              class="shrink-0 w-9 h-9 rounded-lg flex items-center justify-center"
              :class="section.available
                ? 'bg-violet-50 dark:bg-violet-500/10'
                : 'bg-gray-100 dark:bg-gray-700'"
            >
              <component
                :is="section.icon"
                class="w-5 h-5"
                :class="section.available
                  ? 'text-violet-500'
                  : 'text-gray-400'"
              />
            </div>

            <div class="flex-1 min-w-0">
              <div class="flex items-center gap-2 mb-0.5">
                <h3
                  class="font-medium text-sm"
                  :class="section.available
                    ? 'text-gray-800 dark:text-gray-100 group-hover:text-violet-600 dark:group-hover:text-violet-400 transition'
                    : 'text-gray-500 dark:text-gray-400'"
                >
                  {{ section.label }}
                </h3>
                <span
                  v-if="!section.available"
                  class="text-[10px] font-semibold tracking-wide uppercase px-1.5 py-0.5 rounded bg-gray-100 dark:bg-gray-700 text-gray-400 dark:text-gray-500"
                >
                  Soon
                </span>
              </div>
              <p class="text-xs text-gray-500 dark:text-gray-500 leading-relaxed">
                {{ section.description }}
              </p>
            </div>

            <IconChevronRight
              v-if="section.available"
              class="shrink-0 w-4 h-4 text-gray-300 dark:text-gray-600 group-hover:text-violet-400 dark:group-hover:text-violet-500 transition mt-0.5"
            />
          </div>
        </button>
      </div>
    </template>

  </div>
</template>

<script setup lang="ts">
import { computed, ref, watch, type Component } from 'vue'
import { useRouter } from 'vue-router'
import {
  IconArrowLeft,
  IconChevronRight,
  IconBuilding,
  IconDatabase,
  IconShieldLock,
  IconUsers,
  IconPlug,
} from '@tabler/icons-vue'
import TenantProfileSettings from './TenantProfileSettings.vue'
import TenantStorageSettings from './TenantStorageSettings.vue'
import TenantMembershipSettings from './TenantMembershipSettings.vue'
import TenantSSOSettings from './TenantSSOSettings.vue'

const props = withDefaults(defineProps<{
  initialSection?: string | null
}>(), {
  initialSection: null,
})

const router = useRouter()

type SectionEntry = {
  id: string
  label: string
  description: string
  icon: Component
  available: boolean
}

const SECTIONS: SectionEntry[] = [
  {
    id: 'profile',
    label: 'Tenant Profile',
    description: 'View workspace identity details such as name, slug, and your effective role.',
    icon: IconBuilding,
    available: true,
  },
  {
    id: 'storage',
    label: 'Storage Classes',
    description: 'Review storage classes available within this tenant and the default assignment.',
    icon: IconDatabase,
    available: true,
  },
  {
    id: 'sso',
    label: 'Single Sign-On (SSO)',
    description: 'Configure tenant sign-in providers. LDAP is temporarily disabled for tenant scope.',
    icon: IconShieldLock,
    available: true,
  },
  {
    id: 'membership',
    label: 'Membership & Access',
    description: 'Manage personal membership actions, including safely leaving this tenant.',
    icon: IconUsers,
    available: true,
  },
  {
    id: 'integrations',
    label: 'Tenant Integrations',
    description: 'Configure per-tenant integration hooks and notification endpoints.',
    icon: IconPlug,
    available: false,
  },
]

const selectedSection = ref<string | null>(props.initialSection ?? null)

watch(
  () => props.initialSection,
  (val) => { selectedSection.value = val ?? null },
)

function openSection(id: string) {
  selectedSection.value = id
  router.push({ hash: '#' + id })
}

function goBack() {
  selectedSection.value = null
  router.push({ hash: '#general' })
}

const currentEntry = computed(() => SECTIONS.find(s => s.id === selectedSection.value))
const currentSectionLabel = computed(() => currentEntry.value?.label ?? '')
const currentSectionDescription = computed(() => currentEntry.value?.description ?? '')
const currentSectionIcon = computed(() => currentEntry.value?.icon)
</script>
