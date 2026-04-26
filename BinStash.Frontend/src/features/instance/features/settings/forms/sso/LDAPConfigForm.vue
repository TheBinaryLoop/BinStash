<template>
  <div class="space-y-4">
    <!-- URL -->
    <div>
      <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
        LDAP URL <span class="text-rose-500">*</span>
      </label>
      <input
        :value="modelValue.url"
        @input="update('url', ($event.target as HTMLInputElement).value)"
        type="text"
        placeholder="ldap://server.example.com:389 or ldaps://…"
        class="form-input w-full text-sm"
      />
      <p class="mt-1 text-xs text-gray-400 dark:text-gray-500">
        Use <code class="font-mono">ldaps://</code> for encrypted connections (port 636 is typical).
      </p>
    </div>

    <!-- Bind DN + Bind Password row -->
    <div class="grid grid-cols-1 sm:grid-cols-2 gap-4">
      <div>
        <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
          Bind DN
        </label>
        <input
          :value="modelValue.bindDN"
          @input="update('bindDN', ($event.target as HTMLInputElement).value)"
          type="text"
          placeholder="cn=admin,dc=example,dc=com"
          autocomplete="off"
          class="form-input w-full text-sm"
        />
        <p class="mt-1 text-xs text-gray-400 dark:text-gray-500">
          Leave blank to use anonymous bind.
        </p>
      </div>
      <div>
        <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
          Bind Password
        </label>
        <div class="relative">
          <input
            :value="modelValue.bindPassword"
            @input="update('bindPassword', ($event.target as HTMLInputElement).value)"
            :type="showPassword ? 'text' : 'password'"
            placeholder="••••••••"
            autocomplete="new-password"
            class="form-input w-full text-sm pr-10"
          />
          <button
            type="button"
            @click="showPassword = !showPassword"
            class="absolute inset-y-0 right-0 px-3 flex items-center text-gray-400 hover:text-gray-600 dark:hover:text-gray-300"
            tabindex="-1"
          >
            <IconEye v-if="!showPassword" class="w-4 h-4" />
            <IconEyeOff v-else class="w-4 h-4" />
          </button>
        </div>
        <p v-if="isMasked(modelValue.bindPassword)" class="mt-1 text-xs text-amber-600 dark:text-amber-400 flex items-center gap-1">
          <IconLock class="w-3.5 h-3.5 shrink-0" />
          A password is saved. Enter a new value to replace it, or leave as-is.
        </p>
      </div>
    </div>

    <!-- Base DN -->
    <div>
      <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
        Base DN <span class="text-rose-500">*</span>
      </label>
      <input
        :value="modelValue.baseDN"
        @input="update('baseDN', ($event.target as HTMLInputElement).value)"
        type="text"
        placeholder="dc=example,dc=com"
        class="form-input w-full text-sm"
      />
      <p class="mt-1 text-xs text-gray-400 dark:text-gray-500">
        The root directory node where user searches begin.
      </p>
    </div>

    <!-- User Filter -->
    <div>
      <label class="block text-xs font-medium text-gray-500 dark:text-gray-400 mb-1">
        User Filter
      </label>
      <input
        :value="modelValue.userFilter"
        @input="update('userFilter', ($event.target as HTMLInputElement).value)"
        type="text"
        placeholder="(objectClass=person)"
        class="form-input w-full text-sm font-mono"
      />
      <p class="mt-1 text-xs text-gray-400 dark:text-gray-500">
        LDAP filter applied when searching for users. Use <code class="font-mono">{username}</code> as a placeholder for the login name.
      </p>
    </div>

    <!-- ── Permission Mapping ──────────────────────────────────────────────── -->
    <div class="pt-4 border-t border-gray-100 dark:border-gray-700/60 space-y-5">

      <!-- Section header -->
      <div>
        <h3 class="text-xs font-semibold uppercase tracking-wide text-gray-400 dark:text-gray-500 mb-0.5">
          Permission Mapping
        </h3>
        <p class="text-xs text-gray-400 dark:text-gray-500">
          Map LDAP group memberships to application roles. Users that belong to a listed group
          DN receive the corresponding role automatically upon sign-in.
        </p>
      </div>

      <!-- Single-tenant hint -->
      <p v-if="tenancyMode === 'Single'" class="flex items-start gap-1.5 text-xs text-sky-600 dark:text-sky-400 bg-sky-50 dark:bg-sky-500/10 border border-sky-200 dark:border-sky-500/30 rounded-lg px-3 py-2">
        <IconInfoCircle class="w-3.5 h-3.5 shrink-0 mt-0.5" />
        Running in <strong class="mx-0.5">single-tenant</strong> mode — tenant role mappings apply to the configured default tenant automatically.
      </p>

      <!-- ── Instance Administrators ──────────────────────────────────────── -->
      <div class="space-y-2">
        <div class="flex items-center gap-2 flex-wrap">
          <span class="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-xs font-semibold bg-violet-100 dark:bg-violet-500/20 text-violet-700 dark:text-violet-300">
            <IconShield class="w-3 h-3" />
            Instance Admin
          </span>
          <span class="text-xs text-gray-400 dark:text-gray-500">full control over the entire instance</span>
        </div>

        <div v-if="instanceAdminRows.length > 0" class="space-y-2">
          <div
            v-for="row in instanceAdminRows"
            :key="row.id"
            class="flex items-center gap-2"
          >
            <input
              v-model="row.groupDN"
              @input="emitMapping"
              type="text"
              placeholder="cn=instance-admins,dc=example,dc=com"
              class="form-input flex-1 text-sm font-mono"
            />
            <button
              type="button"
              @click="removeInstanceAdminRow(row.id)"
              class="p-1.5 text-gray-400 hover:text-rose-500 dark:hover:text-rose-400 transition rounded shrink-0"
              title="Remove"
            >
              <IconX class="w-4 h-4" />
            </button>
          </div>
        </div>
        <p v-else class="text-xs text-gray-400 dark:text-gray-500 italic">
          No groups configured — InstanceAdmin cannot be granted via SSO.
        </p>

        <button
          type="button"
          @click="addInstanceAdminRow"
          class="flex items-center gap-1.5 text-xs font-medium text-violet-600 dark:text-violet-400 hover:text-violet-700 dark:hover:text-violet-300 transition"
        >
          <IconPlus class="w-3.5 h-3.5" />
          Add group
        </button>
      </div>

      <!-- ── Tenant Administrators ─────────────────────────────────────────── -->
      <div class="space-y-2">
        <div class="flex items-center gap-2 flex-wrap">
          <span class="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-xs font-semibold bg-amber-100 dark:bg-amber-500/20 text-amber-700 dark:text-amber-300">
            <IconBuildingCommunity class="w-3 h-3" />
            Tenant Admin
          </span>
          <span class="text-xs text-gray-400 dark:text-gray-500">admin rights within a tenant</span>
        </div>

        <!-- No tenants warning (multi-tenant only) -->
        <div
          v-if="tenancyMode === 'Multi' && effectiveTenants.length === 0"
          class="flex items-center gap-2 text-xs text-amber-600 dark:text-amber-400 bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/30 rounded-lg px-3 py-2"
        >
          <IconAlertTriangle class="w-3.5 h-3.5 shrink-0" />
          No tenants found. Create a tenant before configuring tenant role mappings.
        </div>

        <template v-else>
          <div v-if="tenantAdminRows.length > 0" class="space-y-2">
            <div
              v-for="row in tenantAdminRows"
              :key="row.id"
              class="flex items-center gap-2 flex-wrap sm:flex-nowrap"
            >
              <input
                v-model="row.groupDN"
                @input="emitMapping"
                type="text"
                placeholder="cn=tenant-admins,dc=example,dc=com"
                class="form-input flex-1 min-w-0 text-sm font-mono"
              />
              <select
                v-if="tenancyMode === 'Multi'"
                v-model="row.tenantId"
                @change="emitMapping"
                class="form-select text-sm w-full sm:w-44 shrink-0"
              >
                <option value="" disabled>Select tenant…</option>
                <option v-for="t in effectiveTenants" :key="t.tenantId" :value="t.tenantId">
                  {{ t.name }}
                </option>
              </select>
              <button
                type="button"
                @click="removeTenantAdminRow(row.id)"
                class="p-1.5 text-gray-400 hover:text-rose-500 dark:hover:text-rose-400 transition rounded shrink-0"
                title="Remove"
              >
                <IconX class="w-4 h-4" />
              </button>
            </div>
          </div>
          <p v-else class="text-xs text-gray-400 dark:text-gray-500 italic">
            No groups configured — TenantAdmin cannot be granted via SSO.
          </p>

          <button
            type="button"
            @click="addTenantAdminRow"
            class="flex items-center gap-1.5 text-xs font-medium text-violet-600 dark:text-violet-400 hover:text-violet-700 dark:hover:text-violet-300 transition"
          >
            <IconPlus class="w-3.5 h-3.5" />
            Add group
          </button>
        </template>
      </div>

      <!-- ── Tenant Members ────────────────────────────────────────────────── -->
      <div class="space-y-2">
        <div class="flex items-center gap-2 flex-wrap">
          <span class="inline-flex items-center gap-1.5 px-2 py-0.5 rounded-md text-xs font-semibold bg-sky-100 dark:bg-sky-500/20 text-sky-700 dark:text-sky-300">
            <IconUsers class="w-3 h-3" />
            Tenant Member
          </span>
          <span class="text-xs text-gray-400 dark:text-gray-500">regular member access within a tenant</span>
        </div>

        <!-- No tenants warning (multi-tenant only) -->
        <div
          v-if="tenancyMode === 'Multi' && effectiveTenants.length === 0"
          class="flex items-center gap-2 text-xs text-amber-600 dark:text-amber-400 bg-amber-50 dark:bg-amber-500/10 border border-amber-200 dark:border-amber-500/30 rounded-lg px-3 py-2"
        >
          <IconAlertTriangle class="w-3.5 h-3.5 shrink-0" />
          No tenants found. Create a tenant before configuring tenant role mappings.
        </div>

        <template v-else>
          <div v-if="tenantMemberRows.length > 0" class="space-y-2">
            <div
              v-for="row in tenantMemberRows"
              :key="row.id"
              class="flex items-center gap-2 flex-wrap sm:flex-nowrap"
            >
              <input
                v-model="row.groupDN"
                @input="emitMapping"
                type="text"
                placeholder="cn=tenant-members,dc=example,dc=com"
                class="form-input flex-1 min-w-0 text-sm font-mono"
              />
              <select
                v-if="tenancyMode === 'Multi'"
                v-model="row.tenantId"
                @change="emitMapping"
                class="form-select text-sm w-full sm:w-44 shrink-0"
              >
                <option value="" disabled>Select tenant…</option>
                <option v-for="t in effectiveTenants" :key="t.tenantId" :value="t.tenantId">
                  {{ t.name }}
                </option>
              </select>
              <button
                type="button"
                @click="removeTenantMemberRow(row.id)"
                class="p-1.5 text-gray-400 hover:text-rose-500 dark:hover:text-rose-400 transition rounded shrink-0"
                title="Remove"
              >
                <IconX class="w-4 h-4" />
              </button>
            </div>
          </div>
          <p v-else class="text-xs text-gray-400 dark:text-gray-500 italic">
            No groups configured — TenantMember cannot be granted via SSO.
          </p>

          <button
            type="button"
            @click="addTenantMemberRow"
            class="flex items-center gap-1.5 text-xs font-medium text-violet-600 dark:text-violet-400 hover:text-violet-700 dark:hover:text-violet-300 transition"
          >
            <IconPlus class="w-3.5 h-3.5" />
            Add group
          </button>
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