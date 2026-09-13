<script setup lang="ts">
/**
 * Moves a repository into another workspace.
 *
 * The picker is server-driven (`repositoryMoveTargets`) rather than derived from the
 * workspace switcher: a move only works into a workspace the caller administers *and* that
 * is configured for this repository's chunk store, and only the server can tell.
 */
import { AlertTriangle } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { toast } from 'vue-sonner'

import { Alert, AlertDescription, AlertTitle } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useMutation, useQuery } from '@/composables/useGraphql'
import { MoveRepositoryDocument, RepositoryMoveTargetsDocument } from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'

const props = defineProps<{ repoId: string; repoName: string }>()
const open = defineModel<boolean>('open', { default: false })
const emit = defineEmits<{ moved: [tenantId: string] }>()

const selected = ref<string | undefined>(undefined)

const targetsQuery = useQuery(
  RepositoryMoveTargetsDocument,
  () => ({ repoId: props.repoId }),
  { enabled: open, fetchPolicy: 'network-only' },
)

const targets = computed(() => targetsQuery.result.value?.repositoryMoveTargets ?? [])
const available = computed(() => targets.value.filter((target) => !target.nameConflict))
const selectedTarget = computed(() =>
  targets.value.find((target) => target.tenantId === selected.value),
)

// A stale selection would survive the dialog closing and reopening on another repository.
watch(open, (isOpen) => {
  if (!isOpen) selected.value = undefined
})

const { mutate: moveRepository, loading: moving } = useMutation(MoveRepositoryDocument, {
  refetchQueries: ['Repositories'],
})

async function submit() {
  const target = selectedTarget.value
  if (!target) return

  try {
    await moveRepository({ input: { repoId: props.repoId, targetTenantId: target.tenantId } })
    open.value = false
    toast.success(`“${props.repoName}” moved to ${target.tenantName}.`)
    emit('moved', target.tenantId)
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not move the repository.'))
  }
}
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>Move repository</DialogTitle>
        <DialogDescription>
          Move “{{ props.repoName }}” — and every release in it — to another workspace. The stored
          data is not copied, so the move is immediate.
        </DialogDescription>
      </DialogHeader>

      <div class="space-y-4">
        <div class="space-y-2">
          <Label for="move-target">Destination workspace</Label>
          <Select v-model="selected" :disabled="moving || targetsQuery.loading.value">
            <SelectTrigger id="move-target" class="w-full">
              <SelectValue
                :placeholder="targetsQuery.loading.value ? 'Loading…' : 'Select a workspace'"
              />
            </SelectTrigger>
            <SelectContent>
              <SelectItem
                v-for="target in targets"
                :key="target.tenantId"
                :value="target.tenantId"
                :disabled="target.nameConflict"
              >
                {{ target.tenantName }}
                <span v-if="target.nameConflict" class="text-muted-foreground">
                  (name already taken)
                </span>
                <span v-else class="text-muted-foreground font-mono text-xs">
                  {{ target.storageClassName }}
                </span>
              </SelectItem>
            </SelectContent>
          </Select>
        </div>

        <p
          v-if="!targetsQuery.loading.value && !targets.length"
          class="text-muted-foreground text-sm"
        >
          There is no workspace to move this repository to. A destination has to be one you
          administer and one that is configured for this repository's chunk store.
        </p>

        <p
          v-else-if="!targetsQuery.loading.value && !available.length"
          class="text-muted-foreground text-sm"
        >
          Every eligible workspace already holds a repository called “{{ props.repoName }}”. Rename
          one of them first.
        </p>

        <Alert v-if="selectedTarget" variant="destructive">
          <AlertTriangle />
          <AlertTitle>Access is not carried over</AlertTitle>
          <AlertDescription>
            Per-repository grants are revoked, so members and service accounts of this workspace —
            including any CI job publishing to it — lose access. Admins of
            {{ selectedTarget.tenantName }} grant access again from there. Storage also starts
            counting towards that workspace's usage.
          </AlertDescription>
        </Alert>
      </div>

      <DialogFooter>
        <Button variant="outline" :disabled="moving" @click="open = false">Cancel</Button>
        <Button variant="destructive" :disabled="moving || !selectedTarget" @click="submit">
          {{ moving ? 'Moving…' : 'Move repository' }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
