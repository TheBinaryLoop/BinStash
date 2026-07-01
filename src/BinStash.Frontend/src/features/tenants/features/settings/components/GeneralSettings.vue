<template>
  <div class="space-y-6">

    <!-- ── Detail view ──────────────────────────────────────────────────── -->
    <template v-if="selectedSection">
      <BaseButton variant="ghost" size="sm" :icon="IconArrowLeft" @click="goBack">
        Back to Tenant Settings
      </BaseButton>

      <TenantProfileSettings v-if="selectedSection === 'profile'" />
      <TenantStorageSettings v-else-if="selectedSection === 'storage'" />
      <TenantSSOSettings v-else-if="selectedSection === 'sso'" />
      <TenantMembershipSettings v-else-if="selectedSection === 'membership'" />

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
      <div>
        <h2 class="text-lg font-semibold text-ink-strong">General Settings</h2>
        <p class="mt-0.5 text-sm text-ink-muted">
          Configure tenant profile, storage visibility, and member self-service controls.
        </p>
      </div>

      <div class="grid grid-cols-1 gap-4 md:grid-cols-2">
        <button
          v-for="section in SECTIONS"
          :key="section.id"
          @click="section.available ? openSection(section.id) : undefined"
          :disabled="!section.available"
          class="group rounded-card border p-5 text-left transition"
          :class="section.available
            ? 'border-hairline bg-card hover:border-accent/40 hover:shadow-sm cursor-pointer'
            : 'border-dashed border-hairline bg-card opacity-60 cursor-default'"
        >
          <div class="flex items-start gap-3">
            <div
              class="flex h-10 w-10 shrink-0 items-center justify-center rounded-xl"
              :class="section.available ? 'bg-accent-soft text-accent' : 'bg-raised text-ink-subtle'"
            >
              <component :is="section.icon" class="h-5 w-5" />
            </div>

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
                <BaseBadge v-if="!section.available" tone="neutral">Soon</BaseBadge>
              </div>
              <p class="text-xs leading-relaxed text-ink-muted">
                {{ section.description }}
              </p>
            </div>

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
import { BaseButton, BaseCard, BaseBadge, EmptyState } from '@/shared/components/ui'

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
