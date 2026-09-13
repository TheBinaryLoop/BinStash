<script setup lang="ts">
import { ArrowRight, Package, Settings2, ShieldCheck } from '@lucide/vue'
import { computed, ref } from 'vue'
import { useRoute } from 'vue-router'

import AsyncSection from '@/components/app/AsyncSection.vue'
import CopyButton from '@/components/app/CopyButton.vue'
import EmptyState from '@/components/app/EmptyState.vue'
import PageBreadcrumbs from '@/components/app/PageBreadcrumbs.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { useQuery } from '@/composables/useGraphql'
import {
  RepositoryAccessDocument,
  RepositoryConfigDocument,
  RepositoryDocument,
  RepositoryReleasesDocument,
} from '@/graphql/generated'
import { formatBytes, formatDate, formatNumber, formatRelative } from '@/lib/format'
import { useTenantStore } from '@/stores/tenant'

const route = useRoute()
const tenants = useTenantStore()

const repoId = computed(() => route.params.repoId as string)
const params = computed(() => ({ tenantId: tenants.activeTenantId, repoId: repoId.value }))

const tab = ref('releases')

const repository = useQuery(RepositoryDocument, () => ({ id: repoId.value }))
const repo = computed(() => repository.result.value?.repository)

const PAGE_SIZE = 25
const releaseVariables = computed(() => ({
  id: repoId.value,
  first: PAGE_SIZE,
  order: [{ createdAt: 'DESC' as const }],
}))
const releases = useQuery(RepositoryReleasesDocument, releaseVariables)
const releaseNodes = computed(() => releases.result.value?.repository?.releases?.nodes ?? [])
const releasePageInfo = computed(() => releases.result.value?.repository?.releases?.pageInfo)
const loadingMore = ref(false)

async function loadMoreReleases() {
  if (!releasePageInfo.value?.hasNextPage) return
  loadingMore.value = true
  try {
    await releases.fetchMore({ ...releaseVariables.value, after: releasePageInfo.value.endCursor })
  } finally {
    loadingMore.value = false
  }
}

// Config and access are separate authorize-guarded fields; only fetch them when their tab
// is opened, so a plain member viewing releases never trips the admin-only access check.
const config = useQuery(
  RepositoryConfigDocument,
  () => ({ id: repoId.value }),
  { enabled: computed(() => tab.value === 'configuration') },
)
const access = useQuery(
  RepositoryAccessDocument,
  () => ({ id: repoId.value }),
  { enabled: computed(() => tab.value === 'access') },
)

const dedupe = computed(() => config.result.value?.repository?.config?.dedupeConfig)
const accessEntries = computed(() => access.result.value?.repository?.access ?? [])

const subjectLabels: Record<number, string> = { 0: 'User', 1: 'Service account', 2: 'Group' }

const cliCommand = computed(() =>
  repo.value ? `binstash release add --repo ${repo.value.name} --version <version> <path>` : '',
)
</script>

<template>
  <div class="space-y-6">
    <PageBreadcrumbs
      :items="[
        { label: 'Repositories', to: { name: 'repositories', params: { tenantId: tenants.activeTenantId } } },
        { label: repo?.name ?? '…' },
      ]"
    />

    <AsyncSection
      :loading="repository.loading.value"
      :error="repository.error.value"
      :has-data="!!repo"
      :skeleton-rows="2"
      @retry="repository.refetch()"
    >
      <PageHeader
        v-if="repo"
        :title="repo.name"
        :description="repo.description ?? undefined"
      >
        <template #badge>
          <Badge variant="secondary" class="font-mono">{{ repo.storageClass }}</Badge>
        </template>
        <template #meta>
          <div class="text-muted-foreground flex flex-wrap items-center gap-x-4 gap-y-1 text-xs">
            <span class="flex items-center gap-1.5">
              <span class="font-mono">{{ repo.id.slice(0, 8) }}</span>
              <CopyButton :value="repo.id" label="ID" />
            </span>
            <span>Created {{ formatDate(repo.createdAt) }}</span>
          </div>
        </template>
      </PageHeader>
    </AsyncSection>

    <Tabs v-model="tab" class="space-y-4">
      <TabsList>
        <TabsTrigger value="releases" class="gap-1.5">
          <Package class="size-3.5" />
          Releases
        </TabsTrigger>
        <TabsTrigger value="configuration" class="gap-1.5">
          <Settings2 class="size-3.5" />
          Configuration
        </TabsTrigger>
        <TabsTrigger value="access" class="gap-1.5">
          <ShieldCheck class="size-3.5" />
          Access
        </TabsTrigger>
      </TabsList>

      <TabsContent value="releases" class="space-y-4">
        <AsyncSection
          :loading="releases.loading.value"
          :error="releases.error.value"
          :has-data="releaseNodes.length > 0"
          :skeleton-rows="5"
          @retry="releases.refetch()"
        >
          <EmptyState
            v-if="!releaseNodes.length"
            :icon="Package"
            title="No releases yet"
            description="Publish the first release from the CLI or your CI pipeline."
          >
            <code class="bg-muted rounded px-2 py-1 font-mono text-xs">{{ cliCommand }}</code>
          </EmptyState>

          <div v-else class="space-y-4">
            <div class="bg-card hairline overflow-hidden rounded-lg">
              <Table>
                <TableHeader>
                  <TableRow>
                    <TableHead>Version</TableHead>
                    <TableHead>Published</TableHead>
                    <TableHead class="text-right">Size</TableHead>
                    <TableHead class="text-right">Files</TableHead>
                    <TableHead class="text-right">Chunks</TableHead>
                    <TableHead class="w-10" />
                  </TableRow>
                </TableHeader>
                <TableBody>
                  <TableRow
                    v-for="release in releaseNodes"
                    :key="release!.id"
                    class="group cursor-pointer"
                    @click="
                      $router.push({
                        name: 'release',
                        params: { ...params, releaseId: release!.id },
                      })
                    "
                  >
                    <TableCell class="font-mono font-medium">{{ release!.version }}</TableCell>
                    <TableCell class="text-muted-foreground text-sm">
                      {{ formatRelative(release!.createdAt) }}
                    </TableCell>
                    <TableCell class="tabular">
                      {{ formatBytes(release!.metrics?.totalLogicalBytes ?? null) }}
                    </TableCell>
                    <TableCell class="tabular">
                      {{ formatNumber(release!.metrics?.filesInRelease ?? null) }}
                    </TableCell>
                    <TableCell class="tabular">
                      {{ formatNumber(release!.metrics?.chunksInRelease ?? null) }}
                    </TableCell>
                    <TableCell>
                      <ArrowRight
                        class="text-muted-foreground group-hover:text-foreground size-4 transition-colors"
                      />
                    </TableCell>
                  </TableRow>
                </TableBody>
              </Table>
            </div>

            <div v-if="releasePageInfo?.hasNextPage" class="flex justify-center">
              <Button variant="outline" size="sm" :disabled="loadingMore" @click="loadMoreReleases">
                {{ loadingMore ? 'Loading…' : 'Load more' }}
              </Button>
            </div>
          </div>
        </AsyncSection>
      </TabsContent>

      <TabsContent value="configuration">
        <AsyncSection
          :loading="config.loading.value"
          :error="config.error.value"
          :has-data="!!dedupe"
          :skeleton-rows="3"
          @retry="config.refetch()"
        >
          <section v-if="dedupe" class="bg-card hairline space-y-4 rounded-lg p-5">
            <div>
              <h2 class="text-sm font-medium">Deduplication</h2>
              <p class="text-muted-foreground text-xs">
                Inherited from the chunk store behind this repository's storage class. Changing it
                would invalidate every chunk already written, so it is fixed after creation.
              </p>
            </div>

            <dl class="grid gap-x-8 gap-y-3 sm:grid-cols-2">
              <div class="flex justify-between gap-4 text-sm">
                <dt class="text-muted-foreground">Chunker</dt>
                <dd class="font-mono">{{ dedupe.chunker }}</dd>
              </div>
              <div class="flex justify-between gap-4 text-sm">
                <dt class="text-muted-foreground">Minimum chunk</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(dedupe.minChunkSize) }}</dd>
              </div>
              <div class="flex justify-between gap-4 text-sm">
                <dt class="text-muted-foreground">Average chunk</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(dedupe.avgChunkSize) }}</dd>
              </div>
              <div class="flex justify-between gap-4 text-sm">
                <dt class="text-muted-foreground">Maximum chunk</dt>
                <dd class="font-mono tabular-nums">{{ formatBytes(dedupe.maxChunkSize) }}</dd>
              </div>
              <div class="flex justify-between gap-4 text-sm">
                <dt class="text-muted-foreground">Shift count</dt>
                <dd class="font-mono tabular-nums">{{ dedupe.shiftCount ?? '—' }}</dd>
              </div>
              <div class="flex justify-between gap-4 text-sm">
                <dt class="text-muted-foreground">Boundary check</dt>
                <dd class="font-mono tabular-nums">{{ dedupe.boundaryCheckBytes ?? '—' }}</dd>
              </div>
            </dl>
          </section>
        </AsyncSection>
      </TabsContent>

      <TabsContent value="access">
        <AsyncSection
          :loading="access.loading.value"
          :error="access.error.value"
          :has-data="accessEntries.length > 0"
          :skeleton-rows="3"
          @retry="access.refetch()"
        >
          <EmptyState
            v-if="!accessEntries.length"
            :icon="ShieldCheck"
            title="No explicit grants"
            description="Only workspace admins can reach this repository until access is granted."
          />

          <div v-else class="bg-card hairline overflow-hidden rounded-lg">
            <Table>
              <TableHeader>
                <TableRow>
                  <TableHead>Subject</TableHead>
                  <TableHead>Type</TableHead>
                  <TableHead>Role</TableHead>
                  <TableHead>Granted</TableHead>
                </TableRow>
              </TableHeader>
              <TableBody>
                <TableRow v-for="entry in accessEntries" :key="`${entry.subjectType}-${entry.subjectId}`">
                  <TableCell class="font-mono text-xs">{{ entry.subjectId }}</TableCell>
                  <TableCell class="text-sm">
                    {{ subjectLabels[entry.subjectType] ?? entry.subjectType }}
                  </TableCell>
                  <TableCell>
                    <Badge variant="secondary">{{ entry.role }}</Badge>
                  </TableCell>
                  <TableCell class="text-muted-foreground text-sm">
                    {{ formatDate(entry.grantedAt) }}
                  </TableCell>
                </TableRow>
              </TableBody>
            </Table>
          </div>
        </AsyncSection>
      </TabsContent>
    </Tabs>
  </div>
</template>
