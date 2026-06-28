<template>
  <div class="space-y-6">

    <!-- ── Detail view ──────────────────────────────────────────────────── -->
    <template v-if="selectedSection">
      <!-- Back button -->
      <button
        @click="goBack"
        class="flex items-center gap-1.5 text-sm text-gray-500 dark:text-gray-400 hover:text-violet-600 dark:hover:text-violet-400 transition font-medium"
      >
        <IconArrowLeft class="w-4 h-4" />
        Back to General Settings
      </button>

      <!-- Live sections -->
      <EmailSettings v-if="selectedSection === 'email'" />
      <TenancySettings v-else-if="selectedSection === 'tenancy'" />
      <SSOSettings v-else-if="selectedSection === 'sso'" />
      <DomainSettings v-else-if="selectedSection === 'domain'" />

      <!-- Coming-soon sections: generic detail placeholder -->
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
      <!-- Section heading -->
      <div>
        <h2 class="text-lg font-semibold text-gray-800 dark:text-gray-100">General Settings</h2>
        <p class="text-sm text-gray-500 dark:text-gray-400 mt-0.5">
          Configure instance-wide behaviour, email delivery, branding, and authentication.
        </p>
      </div>

      <!-- Settings section cards -->
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
            <!-- Icon badge -->
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

            <!-- Text -->
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

            <!-- Chevron for available sections -->
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

<script lang="ts" setup>
import { ref, computed, watch, type Component } from 'vue'
import { useRouter } from 'vue-router'
import {
  IconArrowLeft,
  IconChevronRight,
  IconMail,
  IconBuilding,
  IconShieldLock,
  IconWorld,
  IconSubtask,
  IconUsers,
} from '@tabler/icons-vue'
import EmailSettings from './EmailSettings.vue'
import TenancySettings from './TenancySettings.vue'
import SSOSettings from './SSOSettings.vue'
import DomainSettings from './DomainSettings.vue'

// ── Props ─────────────────────────────────────────────────────────────────────

const props = withDefaults(defineProps<{
  initialSection?: string | null
}>(), {
  initialSection: null,
})

// ── Router ────────────────────────────────────────────────────────────────────

const router = useRouter()

// ── Section registry ──────────────────────────────────────────────────────────

type SectionEntry = {
  id: string
  label: string
  description: string
  icon: Component
  available: boolean
}

const SECTIONS: SectionEntry[] = [
  {
    id: 'email',
    label: 'Email Configuration',
    description: 'Set up the outgoing email provider for notifications, invitations, and password resets.',
    icon: IconMail,
    available: true,
  },
  {
    id: 'tenancy',
    label: 'Tenancy',
    description: 'Configure single-tenant vs. multi-tenant mode and set the default tenant.',
    icon: IconUsers,
    available: true,
  },
  {
    id: 'branding',
    label: 'Instance Branding',
    description: 'Configure instance name, logo, and custom branding options.',
    icon: IconBuilding,
    available: false,
  },
  {
    id: 'sso',
    label: 'Single Sign-On (SSO)',
    description: 'Configure OIDC / SAML providers for federated authentication.',
    icon: IconShieldLock,
    available: true,
  },
  {
    id: 'domain',
    label: 'Instance URL & Domain',
    description: 'Configure the public URL and custom domain settings for this instance.',
    icon: IconWorld,
    available: true,
  },
  {
    id: 'tasks',
    label: 'Instance Tasks',
    description: 'Manage and monitor instance-level tasks and background jobs.',
    icon: IconSubtask,
    available: false,
  },
]

// ── State ─────────────────────────────────────────────────────────────────────

const selectedSection = ref<string | null>(props.initialSection ?? null)

// Keep in sync when the parent route hash changes (e.g. browser back/forward)
watch(
  () => props.initialSection,
  (val) => { selectedSection.value = val ?? null },
)

// ── Navigation helpers ────────────────────────────────────────────────────────

function openSection(id: string) {
  selectedSection.value = id
  router.push({ hash: '#' + id })
}

function goBack() {
  selectedSection.value = null
  router.push({ hash: '#general' })
}

// ── Derived ───────────────────────────────────────────────────────────────────

const currentEntry = computed(() => SECTIONS.find(s => s.id === selectedSection.value))
const currentSectionLabel = computed(() => currentEntry.value?.label ?? '')
const currentSectionDescription = computed(() => currentEntry.value?.description ?? '')
const currentSectionIcon = computed(() => currentEntry.value?.icon)
</script>