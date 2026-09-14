<script setup lang="ts">
import { FolderGit2, Plus, Search } from '@lucide/vue'
import { useDebounce } from '@vueuse/core'
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
import { Textarea } from '@/components/ui/textarea'
import { useMutation, useQuery } from '@/composables/useGraphql'
import {
  CreateRepositoryDocument,
  RepositoriesDocument,
  TenantStorageClassesDocument,
} from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'
import { formatBytes, formatDateOnly, formatRelative } from '@/lib/format'
import { useTenantStore } from '@/stores/tenant'

const tenants = useTenantStore()
const params = computed(() => ({ tenantId: tenants.activeTenantId }))

const PAGE_SIZE = 24

const search = ref('')
const debouncedSearch = useDebounce(search, 250)
const sort = ref<'recent' | 'name'>('recent')

// Filtering and sorting run server-side — the list is paged, so doing it client-side
// would only ever sort the page you happen to have.
const variables = computed(() => ({
  first: PAGE_SIZE,
  where: debouncedSearch.value.trim()
    ? { name: { contains: debouncedSearch.value.trim() } }
    : undefined,
  order:
    sort.value === 'name'
      ? [{ name: 'ASC' as const }]
      : [{ createdAt: 'DESC' as const }],
}))

const { result, loading, error, refetch, fetchMore } = useQuery(RepositoriesDocument, variables)

const repositories = computed(() => result.value?.repositories?.nodes ?? [])
const totalCount = computed(() => result.value?.repositories?.totalCount ?? 0)
const pageInfo = computed(() => result.value?.repositories?.pageInfo)
const loadingMore = ref(false)

async function loadMore() {
  if (!pageInfo.value?.hasNextPage) return
  loadingMore.value = true
  try {
    await fetchMore({ ...variables.value, after: pageInfo.value.endCursor })
  } finally {
    loadingMore.value = false
  }
}

/* ---- creation ---------------------------------------------------------- */

const createOpen = ref(false)
const newName = ref('')
const newDescription = ref('')
const newStorageClass = ref<string | undefined>(undefined)

const storageClasses = useQuery(TenantStorageClassesDocument, {}, { enabled: createOpen })
const { mutate: createRepository, loading: creating } = useMutation(CreateRepositoryDocument, {
  refetchQueries: ['Repositories'],
})

function openCreate() {
  newName.value = ''
  newDescription.value = ''
  newStorageClass.value = undefined
  createOpen.value = true
}

async function submitCreate() {
  try {
    await createRepository({
      input: {
        name: newName.value.trim(),
        description: newDescription.value.trim() || undefined,
        storageClassName: newStorageClass.value,
      },
    })
    createOpen.value = false
    toast.success(`Repository “${newName.value.trim()}” created.`)
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not create the repository.'))
  }
}
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Repositories" description="Each repository holds the releases of one artifact.">
      <template #badge>
        <Badge v-if="totalCount" variant="secondary" class="font-mono">{{ totalCount }}</Badge>
      </template>
      <template #actions>
        <Button class="gap-2" @click="openCreate">
          <Plus class="size-4" />
          New repository
        </Button>
      </template>
    </PageHeader>

    <div class="flex flex-col gap-2 sm:flex-row sm:items-center">
      <div class="relative flex-1">
        <Search class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2" />
        <Input v-model.trim="search" placeholder="Search repositories…" class="pl-9" />
      </div>

      <Select v-model="sort">
        <SelectTrigger class="sm:w-48">
          <SelectValue />
        </SelectTrigger>
        <SelectContent>
          <SelectItem value="recent">Newest first</SelectItem>
          <SelectItem value="name">Name (A–Z)</SelectItem>
        </SelectContent>
      </Select>
    </div>

    <AsyncSection
      :loading="loading"
      :error="error"
      :has-data="repositories.length > 0"
      :skeleton-rows="6"
      @retry="refetch()"
    >
      <EmptyState
        v-if="!repositories.length"
        :icon="FolderGit2"
        :title="search ? 'No matching repositories' : 'No repositories yet'"
        :description="
          search
            ? 'Try a different search term.'
            : 'Create a repository, then publish releases to it from the CLI or CI.'
        "
      >
        <Button v-if="!search" size="sm" class="gap-2" @click="openCreate">
          <Plus class="size-4" />
          Create a repository
        </Button>
      </EmptyState>

      <div v-else class="space-y-4">
        <ul class="grid gap-3 sm:grid-cols-2 xl:grid-cols-3">
          <li v-for="repo in repositories" :key="repo.id">
            <RouterLink
              :to="{ name: 'repository', params: { ...params, repoId: repo.id } }"
              class="bg-card hairline hover:border-primary/40 flex h-full flex-col gap-3 rounded-lg p-4 transition-colors"
            >
              <div class="flex items-start justify-between gap-2">
                <p class="truncate text-sm font-medium">{{ repo.name }}</p>
                <Badge variant="secondary" class="shrink-0 font-mono text-[0.625rem]">
                  {{ repo.storageClass }}
                </Badge>
              </div>

              <p class="text-muted-foreground line-clamp-2 min-h-8 text-xs">
                {{ repo.description || 'No description.' }}
              </p>

              <div class="border-hairline mt-auto space-y-1.5 border-t pt-3">
                <div class="flex items-baseline justify-between gap-2">
                  <span class="text-muted-foreground text-xs">
                    {{ repo.releases?.totalCount ?? 0 }} release{{
                      (repo.releases?.totalCount ?? 0) === 1 ? '' : 's'
                    }}
                  </span>
                  <!-- Logical size: the quantity this workspace is billed on, and the one that
                       makes "which repository is using the quota?" answerable from the list. -->
                  <span class="font-mono text-xs tabular-nums">
                    {{ formatBytes(repo.metrics?.totalLogicalBytes ?? 0) }}
                  </span>
                </div>
                <div class="text-muted-foreground flex items-baseline justify-between gap-2 font-mono text-xs">
                  <template v-if="repo.releases?.nodes?.[0]">
                    <span class="truncate">{{ repo.releases.nodes[0].version }}</span>
                    <span class="shrink-0">{{ formatRelative(repo.releases.nodes[0].createdAt) }}</span>
                  </template>
                  <template v-else>
                    <span>No releases</span>
                    <span class="shrink-0">{{ formatDateOnly(repo.createdAt) }}</span>
                  </template>
                </div>
              </div>
            </RouterLink>
          </li>
        </ul>

        <div v-if="pageInfo?.hasNextPage" class="flex justify-center">
          <Button variant="outline" size="sm" :disabled="loadingMore" @click="loadMore">
            {{ loadingMore ? 'Loading…' : 'Load more' }}
          </Button>
        </div>
      </div>
    </AsyncSection>

    <Dialog v-model:open="createOpen">
      <DialogContent class="sm:max-w-lg">
        <DialogHeader>
          <DialogTitle>New repository</DialogTitle>
          <DialogDescription>
            The storage class decides which chunk store and chunker the releases use.
          </DialogDescription>
        </DialogHeader>

        <form id="create-repository" class="space-y-4" @submit.prevent="submitCreate">
          <div class="space-y-2">
            <Label for="repo-name">Name</Label>
            <Input id="repo-name" v-model.trim="newName" required :disabled="creating" />
          </div>

          <div class="space-y-2">
            <Label for="repo-description">Description</Label>
            <Textarea
              id="repo-description"
              v-model.trim="newDescription"
              rows="2"
              :disabled="creating"
            />
          </div>

          <div class="space-y-2">
            <Label for="repo-storage-class">Storage class</Label>
            <Select v-model="newStorageClass" :disabled="creating">
              <SelectTrigger id="repo-storage-class" class="w-full">
                <SelectValue placeholder="Workspace default" />
              </SelectTrigger>
              <SelectContent>
                <SelectItem
                  v-for="storageClass in storageClasses.result.value?.tenantStorageClasses ?? []"
                  :key="storageClass.name"
                  :value="storageClass.name"
                >
                  {{ storageClass.name }}
                  <span v-if="storageClass.isDefault" class="text-muted-foreground"> (default)</span>
                </SelectItem>
              </SelectContent>
            </Select>
          </div>
        </form>

        <DialogFooter>
          <Button variant="outline" :disabled="creating" @click="createOpen = false">Cancel</Button>
          <Button
            type="submit"
            form="create-repository"
            :disabled="creating || !newName.trim()"
          >
            {{ creating ? 'Creating…' : 'Create repository' }}
          </Button>
        </DialogFooter>
      </DialogContent>
    </Dialog>
  </div>
</template>
