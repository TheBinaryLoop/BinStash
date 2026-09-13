<script setup lang="ts">
import { useDebounce } from '@vueuse/core'
import { Search } from '@lucide/vue'
import { computed, ref } from 'vue'

import AsyncSection from '@/components/app/AsyncSection.vue'
import AuditTable from '@/components/app/AuditTable.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Input } from '@/components/ui/input'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { useQuery } from '@/composables/useGraphql'
import { AuditLogDocument } from '@/graphql/generated'

const PAGE_SIZE = 30

const search = ref('')
const debouncedSearch = useDebounce(search, 250)
const outcome = ref<'all' | 'SUCCESS' | 'DENIED' | 'FAILED'>('all')

const variables = computed(() => {
  const filters: Record<string, unknown>[] = []
  if (debouncedSearch.value.trim()) {
    filters.push({ action: { contains: debouncedSearch.value.trim() } })
  }
  if (outcome.value !== 'all') {
    filters.push({ outcome: { eq: outcome.value } })
  }

  return {
    first: PAGE_SIZE,
    where: filters.length ? { and: filters } : undefined,
  }
})

const { result, loading, error, refetch, fetchMore } = useQuery(AuditLogDocument, variables)

const entries = computed(() => result.value?.auditLog?.nodes ?? [])
const totalCount = computed(() => result.value?.auditLog?.totalCount ?? 0)
const pageInfo = computed(() => result.value?.auditLog?.pageInfo)
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
</script>

<template>
  <div class="space-y-6">
    <PageHeader
      title="Audit log"
      description="Who changed what in this workspace, and when. Entries are append-only."
    >
      <template #badge>
        <Badge v-if="totalCount" variant="secondary" class="font-mono">{{ totalCount }}</Badge>
      </template>
    </PageHeader>

    <div class="flex flex-col gap-2 sm:flex-row">
      <div class="relative flex-1">
        <Search
          class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2"
        />
        <Input v-model.trim="search" placeholder="Filter by action, e.g. api_key" class="pl-9" />
      </div>

      <Select v-model="outcome">
        <SelectTrigger class="sm:w-44"><SelectValue /></SelectTrigger>
        <SelectContent>
          <SelectItem value="all">All outcomes</SelectItem>
          <SelectItem value="SUCCESS">Success</SelectItem>
          <SelectItem value="DENIED">Denied</SelectItem>
          <SelectItem value="FAILED">Failed</SelectItem>
        </SelectContent>
      </Select>
    </div>

    <AsyncSection
      :loading="loading"
      :error="error"
      :has-data="entries.length > 0"
      :skeleton-rows="6"
      @retry="refetch()"
    >
      <AuditTable
        :entries="entries"
        :has-more="pageInfo?.hasNextPage"
        :loading-more="loadingMore"
        @load-more="loadMore"
      />
    </AsyncSection>
  </div>
</template>
