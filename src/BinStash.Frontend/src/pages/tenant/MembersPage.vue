<script setup lang="ts">
import { MailPlus, Trash2, UserRoundCog, Users } from '@lucide/vue'
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'

import AsyncSection from '@/components/app/AsyncSection.vue'
import ConfirmDialog from '@/components/app/ConfirmDialog.vue'
import EmptyState from '@/components/app/EmptyState.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Checkbox } from '@/components/ui/checkbox'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { useMutation, useQuery } from '@/composables/useGraphql'
import {
  InviteTenantMemberDocument,
  RemoveTenantMemberDocument,
  TenantMembersDocument,
  UpdateTenantMemberRolesDocument,
} from '@/graphql/generated'
import type { TenantMembersQuery } from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'
import { formatDateOnly, initialsOf } from '@/lib/format'
import { useAuthStore } from '@/stores/auth'

type Member = TenantMembersQuery['tenantMembers'][number]

/** Mirrors the role names the server's TenantPermission policies recognise. */
const ASSIGNABLE_ROLES = ['TenantAdmin', 'TenantMember', 'TenantBillingAdmin'] as const

const auth = useAuthStore()
const { result, loading, error, refetch } = useQuery(TenantMembersDocument, {})
const members = computed(() => result.value?.tenantMembers ?? [])

const REFETCH = { refetchQueries: ['TenantMembers'] }
const { mutate: invite, loading: inviting } = useMutation(InviteTenantMemberDocument, REFETCH)
const { mutate: updateRoles, loading: updatingRoles } = useMutation(
  UpdateTenantMemberRolesDocument,
  REFETCH,
)
const { mutate: removeMember } = useMutation(RemoveTenantMemberDocument, REFETCH)

/* ---- invite ------------------------------------------------------------ */

const inviteOpen = ref(false)
const inviteEmail = ref('')
const inviteRoles = ref<string[]>(['TenantMember'])

function openInvite() {
  inviteEmail.value = ''
  inviteRoles.value = ['TenantMember']
  inviteOpen.value = true
}

async function submitInvite() {
  try {
    await invite({ input: { email: inviteEmail.value.trim(), roles: inviteRoles.value } })
    inviteOpen.value = false
    toast.success(`Invitation sent to ${inviteEmail.value.trim()}.`)
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not send the invitation.'))
  }
}

/* ---- roles ------------------------------------------------------------- */

const rolesOpen = ref(false)
const editing = ref<Member | null>(null)
const draftRoles = ref<string[]>([])

function openRoles(member: Member) {
  editing.value = member
  draftRoles.value = [...member.roles]
  rolesOpen.value = true
}

async function submitRoles() {
  if (!editing.value) return
  try {
    await updateRoles({ memberId: editing.value.id, roles: draftRoles.value })
    rolesOpen.value = false
    toast.success('Roles updated.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not update roles.'))
  }
}

/**
 * Takes and returns the array rather than a ref: inside a template `inviteRoles` is
 * already unwrapped, so passing "the ref" would hand this the plain array and the
 * assignment would silently go nowhere.
 */
function withRole(roles: string[], role: string, checked: boolean): string[] {
  return checked ? [...new Set([...roles, role])] : roles.filter((r) => r !== role)
}

/* ---- removal ----------------------------------------------------------- */

const removeOpen = ref(false)
const removing = ref<Member | null>(null)

function openRemove(member: Member) {
  removing.value = member
  removeOpen.value = true
}

async function confirmRemove() {
  if (!removing.value) return
  try {
    await removeMember({ memberId: removing.value.id })
    removeOpen.value = false
    toast.success(`${removing.value.email} removed from the workspace.`)
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not remove the member.'))
  }
}

function isSelf(member: Member) {
  return member.email === auth.user?.email
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Members" description="People with access to this workspace.">
      <template #badge>
        <Badge v-if="members.length" variant="secondary" class="font-mono">
          {{ members.length }}
        </Badge>
      </template>
      <template #actions>
        <Button class="gap-2" @click="openInvite">
          <MailPlus class="size-4" />
          Invite member
        </Button>
      </template>
    </PageHeader>

    <AsyncSection
      :loading="loading"
      :error="error"
      :has-data="members.length > 0"
      :skeleton-rows="4"
      @retry="refetch()"
    >
      <EmptyState
        v-if="!members.length"
        :icon="Users"
        title="No members yet"
        description="Invite teammates to give them access to this workspace."
      >
        <Button size="sm" class="gap-2" @click="openInvite">
          <MailPlus class="size-4" />
          Invite member
        </Button>
      </EmptyState>

      <div v-else class="bg-card hairline overflow-hidden rounded-lg">
        <Table>
          <TableHeader>
            <TableRow>
              <TableHead>Member</TableHead>
              <TableHead>Roles</TableHead>
              <TableHead>Joined</TableHead>
              <TableHead class="w-12" />
            </TableRow>
          </TableHeader>
          <TableBody>
            <TableRow v-for="member in members" :key="member.id">
              <TableCell>
                <div class="flex items-center gap-2.5">
                  <Avatar class="size-7">
                    <AvatarFallback class="bg-secondary text-xs">
                      {{ initialsOf(member.firstName, member.lastName, member.email[0]) }}
                    </AvatarFallback>
                  </Avatar>
                  <div class="min-w-0">
                    <p class="truncate text-sm font-medium">
                      {{ [member.firstName, member.lastName].filter(Boolean).join(' ') || member.email }}
                      <span v-if="isSelf(member)" class="text-muted-foreground font-normal">(you)</span>
                    </p>
                    <p class="text-muted-foreground truncate text-xs">{{ member.email }}</p>
                  </div>
                </div>
              </TableCell>

              <TableCell>
                <div class="flex flex-wrap gap-1">
                  <Badge
                    v-for="role in member.roles"
                    :key="role"
                    :variant="role === 'TenantAdmin' ? 'default' : 'secondary'"
                    class="text-xs"
                  >
                    {{ role.replace('Tenant', '') }}
                  </Badge>
                  <span v-if="!member.roles.length" class="text-muted-foreground text-xs">None</span>
                </div>
              </TableCell>

              <TableCell class="text-muted-foreground text-sm">
                {{ formatDateOnly(member.joinedAt) }}
              </TableCell>

              <TableCell>
                <DropdownMenu>
                  <DropdownMenuTrigger as-child>
                    <Button variant="ghost" size="icon" aria-label="Member actions">
                      <UserRoundCog class="size-4" />
                    </Button>
                  </DropdownMenuTrigger>
                  <DropdownMenuContent align="end">
                    <DropdownMenuItem @select="openRoles(member)">Edit roles</DropdownMenuItem>
                    <DropdownMenuItem
                      v-if="!isSelf(member)"
                      class="text-destructive"
                      @select="openRemove(member)"
                    >
                      Remove from workspace
                    </DropdownMenuItem>
                  </DropdownMenuContent>
                </DropdownMenu>
              </TableCell>
            </TableRow>
          </TableBody>
        </Table>
      </div>
    </AsyncSection>

    <Dialog v-model:open="inviteOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Invite a member</DialogTitle>
          <DialogDescription>
            They will receive an email with a link to join this workspace.
          </DialogDescription>
        </DialogHeader>

        <form id="invite-member" class="space-y-4" @submit.prevent="submitInvite">
          <div class="space-y-2">
            <Label for="invite-email">Email</Label>
            <Input
              id="invite-email"
              v-model.trim="inviteEmail"
              type="email"
              required
              :disabled="inviting"
            />
          </div>

          <div class="space-y-2">
            <Label>Roles</Label>
            <div class="space-y-2">
              <label
                v-for="role in ASSIGNABLE_ROLES"
                :key="role"
                class="flex items-center gap-2 text-sm"
              >
                <Checkbox
                  :model-value="inviteRoles.includes(role)"
                  @update:model-value="(checked) => (inviteRoles = withRole(inviteRoles, role, !!checked))"
                />
                {{ role.replace('Tenant', '') }}
              </label>
            </div>
          </div>
        </form>

        <DialogFooter>
          <Button variant="outline" :disabled="inviting" @click="inviteOpen = false">Cancel</Button>
          <Button
            type="submit"
            form="invite-member"
            :disabled="inviting || !inviteEmail.trim() || !inviteRoles.length"
          >
            {{ inviting ? 'Sending…' : 'Send invitation' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="rolesOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>Edit roles</DialogTitle>
          <DialogDescription>{{ editing?.email }}</DialogDescription>
        </DialogHeader>

        <div class="space-y-2">
          <label v-for="role in ASSIGNABLE_ROLES" :key="role" class="flex items-center gap-2 text-sm">
            <Checkbox
              :model-value="draftRoles.includes(role)"
              @update:model-value="(checked) => (draftRoles = withRole(draftRoles, role, !!checked))"
            />
            {{ role.replace('Tenant', '') }}
          </label>
        </div>

        <DialogFooter>
          <Button variant="outline" :disabled="updatingRoles" @click="rolesOpen = false">
            Cancel
          </Button>
          <Button :disabled="updatingRoles" @click="submitRoles">
            {{ updatingRoles ? 'Saving…' : 'Save roles' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <ConfirmDialog
      v-model:open="removeOpen"
      title="Remove member?"
      :description="`${removing?.email} will immediately lose access to this workspace.`"
      confirm-label="Remove"
      destructive
      @confirm="confirmRemove"
    >
      <p class="text-muted-foreground text-sm">
        Repositories and releases they published are kept.
      </p>
    </ConfirmDialog>
  </div>
</template>
