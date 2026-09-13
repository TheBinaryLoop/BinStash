<script setup lang="ts">
import { Building2, Search } from '@lucide/vue'
import { useDebounce } from '@vueuse/core'
import { computed, ref } from 'vue'

import AsyncSection from '@/components/app/AsyncSection.vue'
import EmptyState from '@/components/app/EmptyState.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { useQuery } from '@/composables/useGraphql'
import { InstanceTenantsDocument } from '@/graphql/generated'
import { formatDate } from '@/lib/format'
import { useTenantStore } from '@/stores/tenant'

const PAGE_SIZE = 30
const tenants = useTenantStore()

const search = ref('')
const debouncedSearch = useDebounce(search, 250)

const variables = computed(() => ({
  first: PAGE_SIZE,
  where: debouncedSearch.value.trim()
    ? {
        or: [
          { name: { contains: debouncedSearch.value.trim() } },
          { slug: { contains: debouncedSearch.value.trim() } },
        ],
      }
    : undefined,
}))

const { result, loading, error, refetch, fetchMore } = useQuery(InstanceTenantsDocument, variables)

const rows = computed(() => result.value?.tenants?.nodes ?? [])
const totalCount = computed(() => result.value?.tenants?.totalCount ?? 0)
const pageInfo = computed(() => result.value?.tenants?.pageInfo)
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

const memberOf = computed(() => new Set(tenants.tenants.map((tenant) => tenant.id)))
</script>

<template>
  <div class="space-y-6">
    <PageHeader title="Tenants" description="Every workspace on this instance.">
      <template #badge>
        <Badge v-if="totalCount" variant="secondary" class="font-mono">{{ totalCount }}</Badge>
      </template>
    </PageHeader>

    <div class="relative">
      <Search
        class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2"
      />
      <Input v-model.trim="search" placeholder="Search by name or slug…" class="pl-9" />
    </div>

    <AsyncSection
      :loading="loading"
      :error="error"
      :has-data="rows.length > 0"
      :skeleton-rows="5"
      @retry="refetch()"
    >
      <EmptyState
        v-if="!rows.length"
        :icon="Building2"
        :title="search ? 'No matching tenants' : 'No tenants yet'"
      />

      <div v-else class="space-y-4">
        <div class="bg-card hairline overflow-hidden rounded-lg">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>Name</TableHead>
                <TableHead>Slug</TableHead>
                <TableHead>Created</TableHead>
                <TableHead class="w-24" />
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="tenant in rows" :key="tenant.id">
                <TableCell class="font-medium">{{ tenant.name }}</TableCell>
                <TableCell class="font-mono text-xs">{{ tenant.slug }}</TableCell>
                <TableCell class="text-muted-foreground text-sm">
                  {{ formatDate(tenant.createdAt) }}
                </TableCell>
                <TableCell>
                  <!-- Only offer to open workspaces the admin is actually a member of:
                       tenant-scoped queries would be refused otherwise. -->
                  <Button
                    v-if="memberOf.has(tenant.id)"
                    variant="ghost"
                    size="sm"
                    @click="$router.push({ name: 'tenant-home', params: { tenantId: tenant.id } })"
                  >
                    Open
                  </Button>
                  <span v-else class="text-muted-foreground text-xs">Not a member</span>
                </TableCell>
              </TableRow>
            </TableBody>
          </Table>
        </div>

        <div v-if="pageInfo?.hasNextPage" class="flex justify-center">
          <Button variant="outline" size="sm" :disabled="loadingMore" @click="loadMore">
            {{ loadingMore ? 'Loading…' : 'Load more' }}
          </Button>
        </div>
      </div>
    </AsyncSection>
  </div>
</template>
