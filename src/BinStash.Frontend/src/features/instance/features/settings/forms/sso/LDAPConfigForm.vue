<template>
  <div class="space-y-4">
    <!-- URL -->
    <div>
      <BaseInput
        :model-value="modelValue.url"
        @update:model-value="update('url', String($event ?? ''))"
        label="LDAP URL"
        required
        type="text"
        placeholder="ldap://server.example.com:389 or ldaps://…"
      />
      <p class="mt-1 text-xs text-ink-subtle">
        Use <code class="font-mono">ldaps://</code> for encrypted connections (port 636 is typical).
      </p>
    </div>

    <!-- Bind DN + Bind Password row -->
    <div class="grid grid-cols-1 gap-4 sm:grid-cols-2">
      <div>
        <BaseInput
          :model-value="modelValue.bindDN"
          @update:model-value="update('bindDN', String($event ?? ''))"
          label="Bind DN"
          type="text"
          placeholder="cn=admin,dc=example,dc=com"
          autocomplete="off"
        />
        <p class="mt-1 text-xs text-ink-subtle">
          Leave blank to use anonymous bind.
        </p>
      </div>
      <div>
        <BaseInput
          :model-value="modelValue.bindPassword"
          @update:model-value="update('bindPassword', String($event ?? ''))"
          label="Bind Password"
          :type="showPassword ? 'text' : 'password'"
          placeholder="••••••••"
          autocomplete="new-password"
        >
          <template #suffix>
            <button
              type="button"
              @click="showPassword = !showPassword"
              class="flex items-center px-1 text-ink-subtle transition hover:text-ink-strong"
              tabindex="-1"
            >
              <IconEye v-if="!showPassword" class="h-4 w-4" />
              <IconEyeOff v-else class="h-4 w-4" />
            </button>
          </template>
        </BaseInput>
        <p v-if="isMasked(modelValue.bindPassword)" class="mt-1 flex items-center gap-1 text-xs text-warning">
          <IconLock class="h-3.5 w-3.5 shrink-0" />
          A password is saved. Enter a new value to replace it, or leave as-is.
        </p>
      </div>
    </div>

    <!-- Base DN -->
    <div>
      <BaseInput
        :model-value="modelValue.baseDN"
        @update:model-value="update('baseDN', String($event ?? ''))"
        label="Base DN"
        required
        type="text"
        placeholder="dc=example,dc=com"
      />
      <p class="mt-1 text-xs text-ink-subtle">
        The root directory node where user searches begin.
      </p>
    </div>

    <!-- User Filter -->
    <div>
      <BaseInput
        :model-value="modelValue.userFilter"
        @update:model-value="update('userFilter', String($event ?? ''))"
        label="User Filter"
        type="text"
        placeholder="(objectClass=person)"
      />
      <p class="mt-1 text-xs text-ink-subtle">
        LDAP filter applied when searching for users. Use <code class="font-mono">{username}</code> as a placeholder for the login name.
      </p>
    </div>

    <!-- ── Permission Mapping ──────────────────────────────────────────────── -->
    <div class="space-y-5 border-t border-hairline pt-4">

      <!-- Section header -->
      <div>
        <h3 class="mb-0.5 text-xs font-semibold uppercase tracking-wide text-ink-subtle">
          Permission Mapping
        </h3>
        <p class="text-xs text-ink-subtle">
          Map LDAP group memberships to application roles. Users that belong to a listed group
          DN receive the corresponding role automatically upon sign-in.
        </p>
      </div>

      <!-- Single-tenant hint -->
      <p v-if="tenancyMode === 'Single'" class="flex items-start gap-1.5 rounded-control border border-accent/25 bg-accent-soft px-3 py-2 text-xs text-accent">
        <IconInfoCircle class="mt-0.5 h-3.5 w-3.5 shrink-0" />
        Running in <strong class="mx-0.5">single-tenant</strong> mode — tenant role mappings apply to the configured default tenant automatically.
      </p>

      <!-- ── Instance Administrators ──────────────────────────────────────── -->
      <div class="space-y-2">
        <div class="flex flex-wrap items-center gap-2">
          <BaseBadge tone="accent" variant="soft" :icon="IconShield">Instance Admin</BaseBadge>
          <span class="text-xs text-ink-subtle">full control over the entire instance</span>
        </div>

        <div v-if="instanceAdminRows.length > 0" class="space-y-2">
          <div
            v-for="row in instanceAdminRows"
            :key="row.id"
            class="flex items-center gap-2"
          >
            <div class="flex-1">
              <BaseInput
                :model-value="row.groupDN"
                @update:model-value="(v) => { row.groupDN = String(v ?? ''); emitMapping() }"
                type="text"
                placeholder="cn=instance-admins,dc=example,dc=com"
              />
            </div>
            <BaseButton
              variant="ghost"
              size="sm"
              icon-only
              :icon="IconX"
              title="Remove"
              class="shrink-0"
              @click="removeInstanceAdminRow(row.id)"
            />
          </div>
        </div>
        <p v-else class="text-xs italic text-ink-subtle">
          No groups configured — InstanceAdmin cannot be granted via SSO.
        </p>

        <BaseButton variant="subtle" size="sm" :icon="IconPlus" @click="addInstanceAdminRow">
          Add group
        </BaseButton>
      </div>

      <!-- ── Tenant Administrators ─────────────────────────────────────────── -->
      <div class="space-y-2">
        <div class="flex flex-wrap items-center gap-2">
          <BaseBadge tone="warning" variant="soft" :icon="IconBuildingCommunity">Tenant Admin</BaseBadge>
          <span class="text-xs text-ink-subtle">admin rights within a tenant</span>
        </div>

        <!-- No tenants warning (multi-tenant only) -->
        <div
          v-if="tenancyMode === 'Multi' && effectiveTenants.length === 0"
          class="flex items-center gap-2 rounded-control border border-warning/30 bg-warning-soft px-3 py-2 text-xs text-warning"
        >
          <IconAlertTriangle class="h-3.5 w-3.5 shrink-0" />
          No tenants found. Create a tenant before configuring tenant role mappings.
        </div>

        <template v-else>
          <div v-if="tenantAdminRows.length > 0" class="space-y-2">
            <div
              v-for="row in tenantAdminRows"
              :key="row.id"
              class="flex flex-wrap items-center gap-2 sm:flex-nowrap"
            >
              <div class="min-w-0 flex-1">
                <BaseInput
                  :model-value="row.groupDN"
                  @update:model-value="(v) => { row.groupDN = String(v ?? ''); emitMapping() }"
                  type="text"
                  placeholder="cn=tenant-admins,dc=example,dc=com"
                />
              </div>
              <div v-if="tenancyMode === 'Multi'" class="w-full shrink-0 sm:w-44">
                <BaseSelect
                  :model-value="row.tenantId"
                  @update:model-value="(v) => { row.tenantId = String(v ?? ''); emitMapping() }"
                  placeholder="Select tenant…"
                >
                  <option value="" disabled>Select tenant…</option>
                  <option v-for="t in effectiveTenants" :key="t.tenantId" :value="t.tenantId">
                    {{ t.name }}
                  </option>
                </BaseSelect>
              </div>
              <BaseButton
                variant="ghost"
                size="sm"
                icon-only
                :icon="IconX"
                title="Remove"
                class="shrink-0"
                @click="removeTenantAdminRow(row.id)"
              />
            </div>
          </div>
          <p v-else class="text-xs italic text-ink-subtle">
            No groups configured — TenantAdmin cannot be granted via SSO.
          </p>

          <BaseButton variant="subtle" size="sm" :icon="IconPlus" @click="addTenantAdminRow">
            Add group
          </BaseButton>
        </template>
      </div>

      <!-- ── Tenant Members ────────────────────────────────────────────────── -->
      <div class="space-y-2">
        <div class="flex flex-wrap items-center gap-2">
          <BaseBadge tone="accent" variant="soft" :icon="IconUsers">Tenant Member</BaseBadge>
          <span class="text-xs text-ink-subtle">regular member access within a tenant</span>
        </div>

        <!-- No tenants warning (multi-tenant only) -->
        <div
          v-if="tenancyMode === 'Multi' && effectiveTenants.length === 0"
          class="flex items-center gap-2 rounded-control border border-warning/30 bg-warning-soft px-3 py-2 text-xs text-warning"
        >
          <IconAlertTriangle class="h-3.5 w-3.5 shrink-0" />
          No tenants found. Create a tenant before configuring tenant role mappings.
        </div>

        <template v-else>
          <div v-if="tenantMemberRows.length > 0" class="space-y-2">
            <div
              v-for="row in tenantMemberRows"
              :key="row.id"
              class="flex flex-wrap items-center gap-2 sm:flex-nowrap"
            >
              <div class="min-w-0 flex-1">
                <BaseInput
                  :model-value="row.groupDN"
                  @update:model-value="(v) => { row.groupDN = String(v ?? ''); emitMapping() }"
                  type="text"
                  placeholder="cn=tenant-members,dc=example,dc=com"
                />
              </div>
              <div v-if="tenancyMode === 'Multi'" class="w-full shrink-0 sm:w-44">
                <BaseSelect
                  :model-value="row.tenantId"
                  @update:model-value="(v) => { row.tenantId = String(v ?? ''); emitMapping() }"
                  placeholder="Select tenant…"
                >
                  <option value="" disabled>Select tenant…</option>
                  <option v-for="t in effectiveTenants" :key="t.tenantId" :value="t.tenantId">
                    {{ t.name }}
                  </option>
                </BaseSelect>
              </div>
              <BaseButton
                variant="ghost"
                size="sm"
                icon-only
                :icon="IconX"
                title="Remove"
                class="shrink-0"
                @click="removeTenantMemberRow(row.id)"
              />
            </div>
          </div>
          <p v-else class="text-xs italic text-ink-subtle">
            No groups configured — TenantMember cannot be granted via SSO.
          </p>

          <BaseButton variant="subtle" size="sm" :icon="IconPlus" @click="addTenantMemberRow">
            Add group
          </BaseButton>
        </template>
      </div>

    </div>
    <!-- ── end Permission Mapping ─────────────────────────────────────────── -->
  </div>
</template>

<script lang="ts" setup>
import { ref, computed, watch, nextTick, onMounted } from 'vue'
import {
  IconEye,
  IconEyeOff,
  IconLock,
  IconShield,
  IconBuildingCommunity,
  IconUsers,
  IconPlus,
  IconX,
  IconAlertTriangle,
  IconInfoCircle,
} from '@tabler/icons-vue'
import { BaseInput, BaseSelect, BaseButton, BaseBadge } from '@/shared/components/ui'
import {
  MASKED_VALUE,
  type SSOLDAPConfig,
  type SSOLDAPPermissionMapping,
  type TenancyMode,
} from '@/api/instance'
import type { TenantSummaryDto } from '@/stores/tenant'

// ── Props / emits ─────────────────────────────────────────────────────────────

const props = defineProps<{
  modelValue: SSOLDAPConfig
  /** Whether this instance runs in Single or Multi tenant mode. */
  tenancyMode?: TenancyMode
  /** Available tenants - used to populate the tenant selector in multi-tenant mode. */
  tenants?: TenantSummaryDto[]
}>()

const emit = defineEmits<{
  (e: 'update:modelValue', value: SSOLDAPConfig): void
}>()

// ── Prop defaults ─────────────────────────────────────────────────────────────

const effectiveTenants = computed(() => props.tenants ?? [])

// ── Simple field helpers ──────────────────────────────────────────────────────

const showPassword = ref(false)

function isMasked(value: string): boolean {
  return value === MASKED_VALUE
}

function update<K extends keyof SSOLDAPConfig>(field: K, value: SSOLDAPConfig[K]) {
  emit('update:modelValue', { ...props.modelValue, [field]: value })
}

// ── Permission mapping row state ──────────────────────────────────────────────
// Local reactive rows with a stable client-only `id` so v-for keys never
// shift (which would cause inputs to lose focus).
//
// The _selfEmitting flag breaks the feedback loop: when we emit an update the
// parent re-assigns config.ldap which triggers the watch, but we skip the
// re-initialisation because we know the change came from us.

type GroupRow       = { id: string; groupDN: string }
type TenantGroupRow = { id: string; groupDN: string; tenantId: string }

let _rowCounter = 0
function uid(): string {
  return `ldap-perm-${++_rowCounter}`
}

const instanceAdminRows = ref<GroupRow[]>([])
const tenantAdminRows   = ref<TenantGroupRow[]>([])
const tenantMemberRows  = ref<TenantGroupRow[]>([])

let _selfEmitting = false

function syncFromProps(): void {
  const pm = props.modelValue?.permissionMapping
  instanceAdminRows.value = (pm?.instanceAdminGroups ?? []).map(g => ({ id: uid(), groupDN: g }))
  tenantAdminRows.value   = (pm?.tenantAdminMappings ?? []).map(m => ({ id: uid(), groupDN: m.groupDN, tenantId: m.tenantId }))
  tenantMemberRows.value  = (pm?.tenantMemberMappings ?? []).map(m => ({ id: uid(), groupDN: m.groupDN, tenantId: m.tenantId }))
}

// Watch for external modelValue replacement (e.g. parent Reset).
// Shallow watch on the modelValue reference itself is enough: the parent's
// applySnapshot() always assigns a brand-new object so the reference changes.
watch(
  () => props.modelValue,
  () => {
    if (_selfEmitting) return
    syncFromProps()
  },
)

onMounted(syncFromProps)

// ── Emit helpers ──────────────────────────────────────────────────────────────

function emitMapping(): void {
  const pm: SSOLDAPPermissionMapping = {
    instanceAdminGroups: instanceAdminRows.value.map(r => r.groupDN),
    tenantAdminMappings:  tenantAdminRows.value.map(r => ({ groupDN: r.groupDN, tenantId: r.tenantId })),
    tenantMemberMappings: tenantMemberRows.value.map(r => ({ groupDN: r.groupDN, tenantId: r.tenantId })),
  }
  _selfEmitting = true
  emit('update:modelValue', { ...props.modelValue, permissionMapping: pm })
  nextTick(() => { _selfEmitting = false })
}

// ── Row operations ────────────────────────────────────────────────────────────

function addInstanceAdminRow(): void {
  instanceAdminRows.value.push({ id: uid(), groupDN: '' })
  emitMapping()
}
function removeInstanceAdminRow(id: string): void {
  instanceAdminRows.value = instanceAdminRows.value.filter(r => r.id !== id)
  emitMapping()
}

function addTenantAdminRow(): void {
  tenantAdminRows.value.push({ id: uid(), groupDN: '', tenantId: '' })
  emitMapping()
}
function removeTenantAdminRow(id: string): void {
  tenantAdminRows.value = tenantAdminRows.value.filter(r => r.id !== id)
  emitMapping()
}

function addTenantMemberRow(): void {
  tenantMemberRows.value.push({ id: uid(), groupDN: '', tenantId: '' })
  emitMapping()
}
function removeTenantMemberRow(id: string): void {
  tenantMemberRows.value = tenantMemberRows.value.filter(r => r.id !== id)
  emitMapping()
}
</script>
