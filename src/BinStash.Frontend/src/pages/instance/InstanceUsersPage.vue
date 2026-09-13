<script setup lang="ts">
import { Search, Users } from '@lucide/vue'
import { useDebounce } from '@vueuse/core'
import { computed, ref } from 'vue'

import AsyncSection from '@/components/app/AsyncSection.vue'
import EmptyState from '@/components/app/EmptyState.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import { Avatar, AvatarFallback } from '@/components/ui/avatar'
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
import { InstanceUsersDocument } from '@/graphql/generated'
import { initialsOf } from '@/lib/format'

const PAGE_SIZE = 30

const search = ref('')
const debouncedSearch = useDebounce(search, 250)

const variables = computed(() => ({
  first: PAGE_SIZE,
  where: debouncedSearch.value.trim()
    ? {
        or: [
          { email: { contains: debouncedSearch.value.trim() } },
          { firstName: { contains: debouncedSearch.value.trim() } },
          { lastName: { contains: debouncedSearch.value.trim() } },
        ],
      }
    : undefined,
}))

const { result, loading, error, refetch, fetchMore } = useQuery(InstanceUsersDocument, variables)

const rows = computed(() => result.value?.users?.nodes ?? [])
const totalCount = computed(() => result.value?.users?.totalCount ?? 0)
const pageInfo = computed(() => result.value?.users?.pageInfo)
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
    <PageHeader title="Users" description="Every account registered on this instance.">
      <template #badge>
        <Badge v-if="totalCount" variant="secondary" class="font-mono">{{ totalCount }}</Badge>
      </template>
    </PageHeader>

    <div class="relative">
      <Search
        class="text-muted-foreground pointer-events-none absolute top-1/2 left-3 size-4 -translate-y-1/2"
      />
      <Input v-model.trim="search" placeholder="Search by name or email…" class="pl-9" />
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
        :icon="Users"
        :title="search ? 'No matching users' : 'No users yet'"
      />

      <div v-else class="space-y-4">
        <div class="bg-card hairline overflow-hidden rounded-lg">
          <Table>
            <TableHeader>
              <TableRow>
                <TableHead>User</TableHead>
                <TableHead>Email</TableHead>
                <TableHead class="w-40">Status</TableHead>
              </TableRow>
            </TableHeader>
            <TableBody>
              <TableRow v-for="user in rows" :key="user.id">
                <TableCell>
                  <div class="flex items-center gap-2.5">
                    <Avatar class="size-7">
                      <AvatarFallback class="bg-secondary text-xs">
                        {{ initialsOf(user.firstName, user.lastName, user.email[0]) }}
                      </AvatarFallback>
                    </Avatar>
                    <span class="truncate text-sm font-medium">
                      {{ [user.firstName, user.middleName, user.lastName].filter(Boolean).join(' ') }}
                    </span>
                  </div>
                </TableCell>
                <TableCell class="text-muted-foreground text-sm">{{ user.email }}</TableCell>
                <TableCell>
                  <div class="flex flex-wrap gap-1">
                    <Badge :variant="user.isEmailVerified ? 'secondary' : 'destructive'" class="text-xs">
                      {{ user.isEmailVerified ? 'Verified' : 'Unverified' }}
                    </Badge>
                    <Badge v-if="!user.isOnboardingCompleted" variant="outline" class="text-xs">
                      Onboarding
                    </Badge>
                  </div>
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
