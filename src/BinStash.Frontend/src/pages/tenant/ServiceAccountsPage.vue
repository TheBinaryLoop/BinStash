<script setup lang="ts">
import { KeyRound, MoreHorizontal, Plus, ShieldAlert, Trash2 } from '@lucide/vue'
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'

import AsyncSection from '@/components/app/AsyncSection.vue'
import ConfirmDialog from '@/components/app/ConfirmDialog.vue'
import CopyButton from '@/components/app/CopyButton.vue'
import EmptyState from '@/components/app/EmptyState.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Collapsible,
  CollapsibleContent,
  CollapsibleTrigger,
} from '@/components/ui/collapsible'
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
import { useMutation, useQuery } from '@/composables/useGraphql'
import {
  CreateServiceAccountApiKeyDocument,
  CreateServiceAccountDocument,
  DeleteServiceAccountApiKeyDocument,
  DeleteServiceAccountDocument,
  ServiceAccountApiKeysDocument,
  ServiceAccountsDocument,
} from '@/graphql/generated'
import type { ServiceAccountsQuery } from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'
import { formatDate, formatDateOnly, formatRelative } from '@/lib/format'

type ServiceAccount = NonNullable<
  NonNullable<ServiceAccountsQuery['serviceAccounts']>['nodes']
>[number]

const { result, loading, error, refetch } = useQuery(ServiceAccountsDocument, { first: 100 })
const accounts = computed(() => result.value?.serviceAccounts?.nodes ?? [])

const REFETCH = { refetchQueries: ['ServiceAccounts'] }
const { mutate: createAccount, loading: creating } = useMutation(
  CreateServiceAccountDocument,
  REFETCH,
)
const { mutate: deleteAccount } = useMutation(DeleteServiceAccountDocument, REFETCH)

/* ---- account creation -------------------------------------------------- */

const createOpen = ref(false)
const newName = ref('')

async function submitCreate() {
  try {
    await createAccount({ input: { name: newName.value.trim() } })
    createOpen.value = false
    toast.success(`Service account “${newName.value.trim()}” created.`)
    newName.value = ''
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not create the service account.'))
  }
}

/* ---- account deletion -------------------------------------------------- */

const deleteOpen = ref(false)
const deleting = ref<ServiceAccount | null>(null)

async function confirmDelete() {
  if (!deleting.value) return
  try {
    await deleteAccount({ accountId: deleting.value.id })
    deleteOpen.value = false
    toast.success('Service account deleted.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not delete the service account.'))
  }
}

/* ---- API keys ---------------------------------------------------------- */

const expanded = ref<string | null>(null)

const keys = useQuery(
  ServiceAccountApiKeysDocument,
  () => ({ serviceAccountId: expanded.value ?? '' }),
  { enabled: computed(() => expanded.value !== null) },
)

const KEY_REFETCH = { refetchQueries: ['ServiceAccountApiKeys'] }
const { mutate: createKey, loading: creatingKey } = useMutation(
  CreateServiceAccountApiKeyDocument,
  KEY_REFETCH,
)
const { mutate: deleteKey } = useMutation(DeleteServiceAccountApiKeyDocument, KEY_REFETCH)

const keyDialogOpen = ref(false)
const keyAccount = ref<ServiceAccount | null>(null)
const keyName = ref('')
const keyExpiry = ref('')
/** The raw secret is returned exactly once; it cannot be recovered afterwards. */
const issuedKey = ref<string | null>(null)

function openKeyDialog(account: ServiceAccount) {
  keyAccount.value = account
  keyName.value = ''
  keyExpiry.value = ''
  issuedKey.value = null
  keyDialogOpen.value = true
}

async function submitKey() {
  if (!keyAccount.value) return
  try {
    const created = await createKey({
      serviceAccountId: keyAccount.value.id,
      input: {
        displayName: keyName.value.trim(),
        expiresAt: keyExpiry.value ? new Date(keyExpiry.value).toISOString() : undefined,
      },
    })
    issuedKey.value = created?.createServiceAccountApiKey?.key ?? null
    expanded.value = keyAccount.value.id
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not create the API key.'))
  }
}

async function revokeKey(accountId: string, apiKeyId: string) {
  try {
    await deleteKey({ serviceAccountId: accountId, apiKeyId })
    toast.success('API key revoked.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not revoke the API key.'))
  }
}

function toggle(accountId: string) {
  expanded.value = expanded.value === accountId ? null : accountId
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader
      title="Service accounts"
      description="Non-human identities for CI pipelines and automation, authenticated with API keys."
    >
      <template #actions>
        <Button class="gap-2" @click="((newName = ''), (createOpen = true))">
          <Plus class="size-4" />
          New service account
        </Button>
      </template>
    </PageHeader>

    <AsyncSection
      :loading="loading"
      :error="error"
      :has-data="accounts.length > 0"
      :skeleton-rows="3"
      @retry="refetch()"
    >
      <EmptyState
        v-if="!accounts.length"
        :icon="KeyRound"
        title="No service accounts"
        description="Create one to publish releases from CI without using a personal account."
      >
        <Button size="sm" class="gap-2" @click="((newName = ''), (createOpen = true))">
          <Plus class="size-4" />
          New service account
        </Button>
      </EmptyState>

      <ul v-else class="space-y-2">
        <li v-for="account in accounts" :key="account.id">
          <Collapsible
            :open="expanded === account.id"
            class="bg-card hairline overflow-hidden rounded-lg"
            @update:open="toggle(account.id)"
          >
            <div class="flex items-center gap-3 p-4">
              <div
                class="bg-secondary text-muted-foreground flex size-8 shrink-0 items-center justify-center rounded-md"
              >
                <KeyRound class="size-4" />
              </div>

              <div class="min-w-0 flex-1">
                <p class="truncate text-sm font-medium">{{ account.name }}</p>
                <p class="text-muted-foreground text-xs">
                  Created {{ formatDateOnly(account.createdAt) }}
                </p>
              </div>

              <CollapsibleTrigger as-child>
                <Button variant="outline" size="sm">
                  {{ expanded === account.id ? 'Hide keys' : 'API keys' }}
                </Button>
              </CollapsibleTrigger>

              <DropdownMenu>
                <DropdownMenuTrigger as-child>
                  <Button variant="ghost" size="icon" aria-label="Service account actions">
                    <MoreHorizontal class="size-4" />
                  </Button>
                </DropdownMenuTrigger>
                <DropdownMenuContent align="end">
                  <DropdownMenuItem @select="openKeyDialog(account)">
                    Create API key
                  </DropdownMenuItem>
                  <DropdownMenuItem
                    class="text-destructive"
                    @select="((deleting = account), (deleteOpen = true))"
                  >
                    Delete service account
                  </DropdownMenuItem>
                </DropdownMenuContent>
              </DropdownMenu>
            </div>

            <CollapsibleContent>
              <div class="border-hairline space-y-3 border-t p-4">
                <div v-if="keys.loading.value" class="text-muted-foreground text-sm">Loading…</div>

                <p
                  v-else-if="!(keys.result.value?.serviceAccountApiKeys ?? []).length"
                  class="text-muted-foreground text-sm"
                >
                  No API keys yet.
                </p>

                <ul v-else class="space-y-2">
                  <li
                    v-for="key in keys.result.value?.serviceAccountApiKeys ?? []"
                    :key="key.id"
                    class="border-hairline flex items-center gap-3 rounded-md border px-3 py-2"
                  >
                    <div class="min-w-0 flex-1">
                      <p class="truncate text-sm">{{ key.displayName }}</p>
                      <p class="text-muted-foreground text-xs">
                        Created {{ formatDateOnly(key.createdAt) }}
                        <template v-if="key.expiresAt">
                          · expires {{ formatDateOnly(key.expiresAt) }}
                        </template>
                        <template v-if="key.lastUsedAt">
                          · last used {{ formatRelative(key.lastUsedAt) }}
                        </template>
                        <template v-else> · never used</template>
                      </p>
                    </div>

                    <Badge :variant="key.isActive ? 'secondary' : 'destructive'" class="text-xs">
                      {{ key.isActive ? 'Active' : 'Inactive' }}
                    </Badge>

                    <Button
                      variant="ghost"
                      size="icon"
                      class="text-destructive"
                      aria-label="Revoke API key"
                      @click="revokeKey(account.id, key.id)"
                    >
                      <Trash2 class="size-4" />
                    </Button>
                  </li>
                </ul>

                <Button variant="outline" size="sm" class="gap-2" @click="openKeyDialog(account)">
                  <Plus class="size-3.5" />
                  Create API key
                </Button>
              </div>
            </CollapsibleContent>
          </Collapsible>
        </li>
      </ul>
    </AsyncSection>

    <Dialog v-model:open="createOpen">
      <DialogContent class="sm:max-w-md">
        <DialogHeader>
          <DialogTitle>New service account</DialogTitle>
          <DialogDescription>
            Give it a name that identifies the system it acts for.
          </DialogDescription>
        </DialogHeader>

        <form id="create-sa" class="space-y-2" @submit.prevent="submitCreate">
          <Label for="sa-name">Name</Label>
          <Input
            id="sa-name"
            v-model.trim="newName"
            required
            :disabled="creating"
            placeholder="jenkins-ci"
          />
        </form>

        <DialogFooter>
          <Button variant="outline" :disabled="creating" @click="createOpen = false">Cancel</Button>
          <Button type="submit" form="create-sa" :disabled="creating || !newName.trim()">
            {{ creating ? 'Creating…' : 'Create' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>

    <Dialog v-model:open="keyDialogOpen">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>{{ issuedKey ? 'Copy your API key' : 'New API key' }}</DialogTitle>
          <DialogDescription>{{ keyAccount?.name }}</DialogDescription>
        </DialogHeader>

        <template v-if="issuedKey">
          <Alert>
            <ShieldAlert class="size-4" />
            <AlertTitle>This is the only time the key is shown</AlertTitle>
            <AlertDescription>
              BinStash stores only a hash of it. If you lose it, revoke the key and create another.
            </AlertDescription>
          </Alert>

          <div class="flex items-center gap-2">
            <code
              class="bg-muted min-w-0 flex-1 overflow-x-auto rounded p-2.5 font-mono text-xs break-all"
            >
              {{ issuedKey }}
            </code>
            <CopyButton :value="issuedKey" variant="outline" label="Copy" />
          </div>

          <DialogFooter>
            <Button @click="keyDialogOpen = false">Done</Button>
          </DialogFooter>
        </template>

        <template v-else>
          <form id="create-key" class="space-y-4" @submit.prevent="submitKey">
            <div class="space-y-2">
              <Label for="key-name">Label</Label>
              <Input
                id="key-name"
                v-model.trim="keyName"
                required
                :disabled="creatingKey"
                placeholder="jenkins-prod"
              />
            </div>

            <div class="space-y-2">
              <Label for="key-expiry">Expires</Label>
              <Input id="key-expiry" v-model="keyExpiry" type="date" :disabled="creatingKey" />
              <p class="text-muted-foreground text-xs">
                Leave empty for a key that never expires.
              </p>
            </div>
          </form>

          <DialogFooter>
            <Button variant="outline" :disabled="creatingKey" @click="keyDialogOpen = false">
              Cancel
            </Button>
            <Button type="submit" form="create-key" :disabled="creatingKey || !keyName.trim()">
              {{ creatingKey ? 'Creating…' : 'Create key' }}
            </Button>
          </DialogFooter>
        </template>
      </DialogContent>
    </Dialog>

    <ConfirmDialog
      v-model:open="deleteOpen"
      title="Delete service account?"
      :description="`“${deleting?.name}” and all of its API keys will stop working immediately.`"
      confirm-label="Delete"
      destructive
      @confirm="confirmDelete"
    >
      <p class="text-muted-foreground text-sm">
        Any CI pipeline using its keys will start failing.
      </p>
    </ConfirmDialog>
  </div>
</template>
