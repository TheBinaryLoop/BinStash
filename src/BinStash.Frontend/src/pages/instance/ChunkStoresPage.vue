<script setup lang="ts">
import { ArrowRight, Database, Plus } from '@lucide/vue'
import { computed, ref } from 'vue'
import { toast } from 'vue-sonner'

import AsyncSection from '@/components/app/AsyncSection.vue'
import EmptyState from '@/components/app/EmptyState.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Dialog,
  DialogContent,
  DialogDescription,
  DialogFooter,
  DialogHeader,
  DialogTitle,
} from '@/components/ui/dialog'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useMutation, useQuery } from '@/composables/useGraphql'
import {
  ChunkStoresDocument,
  CreateChunkStoreDocument,
  EnabledChunkStoreTypesDocument,
} from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'
import { formatBytes } from '@/lib/format'

const { result, loading, error, refetch } = useQuery(ChunkStoresDocument, { first: 100 })
const stores = computed(() => result.value?.chunkStores?.nodes ?? [])

const createOpen = ref(false)
const types = useQuery(EnabledChunkStoreTypesDocument, {}, { enabled: createOpen })
const { mutate: createStore, loading: creating } = useMutation(CreateChunkStoreDocument, {
  refetchQueries: ['ChunkStores'],
})

const newName = ref('')
const newType = ref<string | undefined>(undefined)
const newPath = ref('')
/**
 * FastCDC defaults, matching the server's. These are immutable once chunks exist,
 * so they are set here and never edited afterwards.
 */
const chunkerMin = ref(2048)
const chunkerAvg = ref(8192)
const chunkerMax = ref(65536)

function openCreate() {
  newName.value = ''
  newType.value = undefined
  newPath.value = ''
  chunkerMin.value = 2048
  chunkerAvg.value = 8192
  chunkerMax.value = 65536
  createOpen.value = true
}

async function submitCreate() {
  try {
    await createStore({
      input: {
        name: newName.value.trim(),
        type: newType.value,
        localPath: newPath.value.trim() || undefined,
        chunker: {
          type: 'FastCdc',
          minChunkSize: chunkerMin.value,
          avgChunkSize: chunkerAvg.value,
          maxChunkSize: chunkerMax.value,
        },
      },
    })
    createOpen.value = false
    toast.success(`Chunk store “${newName.value.trim()}” created.`)
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not create the chunk store.'))
  }
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader
      title="Chunk stores"
      description="Where deduplicated chunks are written. Storage classes map onto these."
    >
      <template #actions>
        <Button class="gap-2" @click="openCreate">
          <Plus class="size-4" />
          New chunk store
        </Button>
      </template>
    </PageHeader>

    <AsyncSection
      :loading="loading"
      :error="error"
      :has-data="stores.length > 0"
      :skeleton-rows="3"
      @retry="refetch()"
    >
      <EmptyState
        v-if="!stores.length"
        :icon="Database"
        title="No chunk stores"
        description="At least one chunk store is required before any release can be published."
      >
        <Button size="sm" class="gap-2" @click="openCreate">
          <Plus class="size-4" />
          New chunk store
        </Button>
      </EmptyState>

      <ul v-else class="grid gap-3 sm:grid-cols-2">
        <li v-for="store in stores" :key="store.id">
          <RouterLink
            :to="{ name: 'chunk-store', params: { chunkStoreId: store.id } }"
            class="bg-card hairline hover:border-primary/40 group flex h-full flex-col gap-3 rounded-lg p-4 transition-colors"
          >
            <div class="flex items-start justify-between gap-2">
              <div class="flex min-w-0 items-center gap-2.5">
                <Database class="text-muted-foreground size-4 shrink-0" />
                <p class="truncate text-sm font-medium">{{ store.name }}</p>
              </div>
              <Badge variant="secondary" class="shrink-0 font-mono text-[0.625rem]">
                {{ store.type }}
              </Badge>
            </div>

            <p
              v-if="store.backendSettings?.localPath"
              class="text-muted-foreground truncate font-mono text-xs"
            >
              {{ store.backendSettings.localPath }}
            </p>

            <dl
              v-if="store.chunker"
              class="border-hairline text-muted-foreground mt-auto grid grid-cols-3 gap-2 border-t pt-3 text-xs"
            >
              <div>
                <dt>Min</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(store.chunker.minChunkSize) }}</dd>
              </div>
              <div>
                <dt>Avg</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(store.chunker.avgChunkSize) }}</dd>
              </div>
              <div>
                <dt>Max</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(store.chunker.maxChunkSize) }}</dd>
              </div>
            </dl>

            <ArrowRight
              class="text-muted-foreground group-hover:text-foreground size-4 self-end transition-colors"
            />
          </RouterLink>
        </li>
      </ul>
    </AsyncSection>

    <Dialog v-model:open="createOpen">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>New chunk store</DialogTitle>
          <DialogDescription>
            Chunker settings are fixed once chunks are written — changing them later would
            invalidate every existing chunk.
          </DialogDescription>
        </DialogHeader>

        <form id="create-store" class="space-y-4" @submit.prevent="submitCreate">
          <div class="space-y-2">
            <Label for="store-name">Name</Label>
            <Input id="store-name" v-model.trim="newName" required :disabled="creating" />
          </div>

          <div class="space-y-2">
            <Label for="store-type">Backend</Label>
            <Select v-model="newType" :disabled="creating">
              <SelectTrigger id="store-type" class="w-full">
                <SelectValue placeholder="Select a backend" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="type in types.result.value?.enabledChunkStoreTypes ?? []"
                  :key="type.name"
                  :value="type.name"
                >
                  {{ type.name }}
                </SelectItem>
              </SelectContent>
            </Select>
          </div>

          <div class="space-y-2">
            <Label for="store-path">Storage path</Label>
            <Input
              id="store-path"
              v-model.trim="newPath"
              class="font-mono"
              :disabled="creating"
              placeholder="/var/lib/binstash/chunks"
            />
          </div>

          <fieldset class="space-y-2">
            <legend class="text-sm font-medium">FastCDC chunker</legend>
            <div class="grid grid-cols-3 gap-2">
              <div class="space-y-1">
                <Label for="chunk-min" class="text-xs">Min bytes</Label>
                <Input
                  id="chunk-min"
                  v-model.number="chunkerMin"
                  type="number"
                  class="font-mono"
                  :disabled="creating"
                />
              </div>
              <div class="space-y-1">
                <Label for="chunk-avg" class="text-xs">Avg bytes</Label>
                <Input
                  id="chunk-avg"
                  v-model.number="chunkerAvg"
                  type="number"
                  class="font-mono"
                  :disabled="creating"
                />
              </div>
              <div class="space-y-1">
                <Label for="chunk-max" class="text-xs">Max bytes</Label>
                <Input
                  id="chunk-max"
                  v-model.number="chunkerMax"
                  type="number"
                  class="font-mono"
                  :disabled="creating"
                />
              </div>
            </div>
          </fieldset>
        </form>

        <DialogFooter>
          <Button variant="outline" :disabled="creating" @click="createOpen = false">Cancel</Button>
          <Button type="submit" form="create-store" :disabled="creating || !newName.trim()">
            {{ creating ? 'Creating…' : 'Create chunk store' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
