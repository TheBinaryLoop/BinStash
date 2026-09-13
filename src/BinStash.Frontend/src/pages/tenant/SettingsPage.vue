<script setup lang="ts">
import { LogOut } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'
import { toast } from 'vue-sonner'

import ConfirmDialog from '@/components/app/ConfirmDialog.vue'
import CopyButton from '@/components/app/CopyButton.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { Separator } from '@/components/ui/separator'
import { useMutation, useQuery } from '@/composables/useGraphql'
import {
  LeaveTenantDocument,
  TenantStorageClassesDocument,
  UpdateTenantDocument,
} from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'
import { formatDate } from '@/lib/format'
import { useTenantStore } from '@/stores/tenant'

const tenants = useTenantStore()
const router = useRouter()

const name = ref('')
const slug = ref('')

// Seed the form once the tenant is known, and re-seed on tenant switch.
watch(
  () => tenants.activeTenant,
  (tenant) => {
    name.value = tenant?.name ?? ''
    slug.value = tenant?.slug ?? ''
  },
  { immediate: true },
)

const dirty = computed(
  () =>
    tenants.activeTenant != null &&
    (name.value !== tenants.activeTenant.name || slug.value !== tenants.activeTenant.slug),
)

const { mutate: updateTenant, loading: saving } = useMutation(UpdateTenantDocument, {
  refetchQueries: ['MyTenants'],
})

async function save() {
  if (!tenants.activeTenantId) return
  try {
    await updateTenant({
      input: { tenantId: tenants.activeTenantId, name: name.value.trim(), slug: slug.value.trim() },
    })
    await tenants.load()
    toast.success('Workspace updated.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not update the workspace.'))
  }
}

const storageClasses = useQuery(TenantStorageClassesDocument, {})

const leaveOpen = ref(false)
const { mutate: leaveTenant } = useMutation(LeaveTenantDocument)

async function confirmLeave() {
  try {
    await leaveTenant({})
    tenants.reset()
    await tenants.load()
    leaveOpen.value = false
    toast.success('You have left the workspace.')
    await router.push({ name: 'select-tenant' })
  } catch (caught) {
    // The server refuses when you are the last admin; surface that verbatim.
    toast.error(errorMessage(caught, 'Could not leave the workspace.'))
  }
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Workspace settings" description="Identity and storage for this workspace." />

    <section class="bg-card hairline space-y-4 rounded-lg p-5">
      <div>
        <h2 class="text-sm font-medium">General</h2>
        <p class="text-muted-foreground text-xs">
          The slug appears in URLs and is what the CLI's <code class="font-mono">--tenant</code> flag
          accepts.
        </p>
      </div>

      <form class="space-y-4" @submit.prevent="save">
        <div class="grid gap-4 sm:grid-cols-2">
          <div class="space-y-2">
            <Label for="tenant-name">Name</Label>
            <Input id="tenant-name" v-model.trim="name" required :disabled="saving" />
          </div>
          <div class="space-y-2">
            <Label for="tenant-slug">Slug</Label>
            <Input id="tenant-slug" v-model.trim="slug" class="font-mono" required :disabled="saving" />
          </div>
        </div>

        <div class="flex items-center gap-3">
          <Button type="submit" :disabled="saving || !dirty">
            {{ saving ? 'Saving…' : 'Save changes' }}
          </Button>
          <span v-if="dirty" class="text-muted-foreground text-xs">Unsaved changes</span>
        </div>
      </form>

      <Separator />

      <dl class="space-y-2 text-sm">
        <div class="flex items-center justify-between gap-4">
          <dt class="text-muted-foreground">Workspace ID</dt>
          <dd class="flex items-center gap-1.5">
            <span class="font-mono text-xs">{{ tenants.activeTenantId }}</span>
            <CopyButton :value="tenants.activeTenantId ?? ''" />
          </dd>
        </div>
        <div class="flex items-center justify-between gap-4">
          <dt class="text-muted-foreground">Created</dt>
          <dd>{{ formatDate(tenants.activeTenant?.createdAt) }}</dd>
        </div>
        <div class="flex items-center justify-between gap-4">
          <dt class="text-muted-foreground">Your roles</dt>
          <dd class="flex flex-wrap justify-end gap-1">
            <Badge
              v-for="role in tenants.activeTenant?.myRoles ?? []"
              :key="role"
              variant="secondary"
              class="text-xs"
            >
              {{ role.replace('Tenant', '') }}
            </Badge>
          </dd>
        </div>
      </dl>
    </section>

    <section class="bg-card hairline space-y-4 rounded-lg p-5">
      <div>
        <h2 class="text-sm font-medium">Storage classes</h2>
        <p class="text-muted-foreground text-xs">
          Available to repositories in this workspace. Configured instance-wide by an administrator.
        </p>
      </div>

      <ul class="space-y-2">
        <li
          v-for="storageClass in storageClasses.result.value?.tenantStorageClasses ?? []"
          :key="storageClass.name"
          class="border-hairline flex items-center gap-3 rounded-md border px-3 py-2"
        >
          <span class="font-mono text-sm">{{ storageClass.name }}</span>
          <span class="text-muted-foreground min-w-0 flex-1 truncate text-xs">
            {{ storageClass.description }}
          </span>
          <Badge v-if="storageClass.isDefault" variant="secondary" class="text-xs">Default</Badge>
        </li>
        <li
          v-if="!(storageClasses.result.value?.tenantStorageClasses ?? []).length"
          class="text-muted-foreground text-sm"
        >
          No storage classes are enabled for this workspace.
        </li>
      </ul>
    </section>

    <section class="border-destructive/30 bg-destructive/5 space-y-4 rounded-lg border p-5">
      <div>
        <h2 class="text-destructive text-sm font-medium">Danger zone</h2>
        <p class="text-muted-foreground text-xs">
          Leaving removes your access. If you are the last admin, the server will refuse.
        </p>
      </div>

      <Button variant="destructive" class="gap-2" @click="leaveOpen = true">
        <LogOut class="size-4" />
        Leave workspace
      </Button>
    </section>

    <ConfirmDialog
      v-model:open="leaveOpen"
      title="Leave this workspace?"
      :description="`You will lose access to ${tenants.activeTenant?.name ?? 'this workspace'} and everything in it.`"
      confirm-label="Leave workspace"
      destructive
      @confirm="confirmLeave"
    />
  </div>
</template>
