<template>
  <div class="space-y-6">

    <!-- ── Detail view ──────────────────────────────────────────────────── -->
    <template v-if="selectedSection">
      <!-- Back button -->
      <BaseButton variant="ghost" size="sm" :icon="IconArrowLeft" @click="goBack">
        Back to General Settings
      </BaseButton>

      <!-- Live sections -->
      <EmailSettings v-if="selectedSection === 'email'" />
      <TenancySettings v-else-if="selectedSection === 'tenancy'" />
      <SSOSettings v-else-if="selectedSection === 'sso'" />
      <DomainSettings v-else-if="selectedSection === 'domain'" />

      <!-- Coming-soon sections: generic detail placeholder -->
      <BaseCard v-else>
        <EmptyState
          :icon="currentSectionIcon"
          :title="`${currentSectionLabel} — Coming Soon`"
          :description="currentSectionDescription"
        />
      </BaseCard>
    </template>

    <!-- ── List view ────────────────────────────────────────────────────── -->
    <template v-else>
      <!-- Section heading -->
      <div>
        <h2 class="text-lg font-semibold text-ink-strong">General Settings</h2>
        <p class="mt-0.5 text-sm text-ink-muted">
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
          class="group rounded-card border bg-card p-5 text-left transition"
          :class="section.available
            ? 'border-hairline hover:border-accent/40 hover:shadow-sm cursor-pointer'
            : 'border-hairline border-dashed opacity-60 cursor-default'"
        >
          <div class="flex items-start gap-3">
            <!-- Icon tile -->
            <div
              class="flex h-9 w-9 shrink-0 items-center justify-center rounded-xl"
              :class="section.available ? 'bg-accent-soft text-accent' : 'bg-raised text-ink-subtle'"
            >
              <component :is="section.icon" class="h-5 w-5" />
            </div>

            <!-- Text -->
            <div class="min-w-0 flex-1">
              <div class="mb-0.5 flex items-center gap-2">
                <h3
                  class="text-sm font-medium"
                  :class="section.available
                    ? 'text-ink-strong transition group-hover:text-accent'
                    : 'text-ink-muted'"
                >
                  {{ section.label }}
                </h3>
                <BaseBadge v-if="!section.available" tone="neutral" size="sm">Soon</BaseBadge>
              </div>
              <p class="text-xs leading-relaxed text-ink-subtle">
                {{ section.description }}
              </p>
            </div>

            <!-- Chevron for available sections -->
            <IconChevronRight
              v-if="section.available"
              class="mt-0.5 h-4 w-4 shrink-0 text-ink-subtle transition group-hover:text-accent"
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
import { BaseButton, BaseBadge, BaseCard, EmptyState } from '@/shared/components/ui'
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
