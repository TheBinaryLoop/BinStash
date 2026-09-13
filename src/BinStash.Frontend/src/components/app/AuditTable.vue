<script setup lang="ts">
import { Bot, CircleSlash, TriangleAlert, User as UserIcon, Wrench } from '@lucide/vue'
import { computed } from 'vue'

import EmptyState from '@/components/app/EmptyState.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import {
  Table,
  TableBody,
  TableCell,
  TableHead,
  TableHeader,
  TableRow,
} from '@/components/ui/table'
import { Tooltip, TooltipContent, TooltipTrigger } from '@/components/ui/tooltip'
import type { AuditEntryFragment } from '@/graphql/generated'
import { formatDate, formatRelative } from '@/lib/format'

const props = defineProps<{
  entries: AuditEntryFragment[]
  hasMore?: boolean
  loadingMore?: boolean
  /** Instance-wide views also show which tenant an entry belongs to. */
  showTenant?: boolean
}>()

const emit = defineEmits<{ loadMore: [] }>()

const actorIcons = {
  USER: UserIcon,
  SERVICE_ACCOUNT: Bot,
  SYSTEM: Wrench,
  ANONYMOUS: CircleSlash,
} as const

/** `repository.access.granted` -> `Repository access granted`. */
function humanizeAction(action: string): string {
  const words = action.replace(/[._]/g, ' ').split(' ').filter(Boolean)
  if (words.length === 0) return action
  return words.join(' ').replace(/^./, (c) => c.toUpperCase())
}

function parseMetadata(metadata: string | null | undefined): Array<[string, string]> {
  if (!metadata) return []
  try {
    const parsed = JSON.parse(metadata) as Record<string, unknown>
    return Object.entries(parsed).map(([key, value]) => [
      key,
      Array.isArray(value) ? value.join(', ') : String(value ?? '—'),
    ])
  } catch {
    // Metadata is free-form JSON written by the server; never let a bad row break the table.
    return []
  }
}

const rows = computed(() =>
  props.entries.map((entry) => ({
    entry,
    metadata: parseMetadata(entry.metadata),
  })),
)
</script>

<template>
  <EmptyState
    v-if="!entries.length"
    :icon="TriangleAlert"
    title="No audited activity yet"
    description="Configuration changes, access grants and credential lifecycle events appear here."
  />

  <div v-else class="space-y-4">
    <div class="bg-card hairline overflow-hidden rounded-lg">
      <Table>
        <TableHeader>
          <TableRow>
            <TableHead class="w-[13rem]">When</TableHead>
            <TableHead class="w-full">Action</TableHead>
            <TableHead class="w-[14rem]">Actor</TableHead>
            <TableHead class="w-[14rem]">Target</TableHead>
            <TableHead v-if="showTenant" class="w-[10rem]">Tenant</TableHead>
            <TableHead class="w-[7rem]">Outcome</TableHead>
          </TableRow>
        </TableHeader>

        <TableBody>
          <TableRow v-for="{ entry, metadata } in rows" :key="entry.id" class="align-middle">
            <TableCell class="py-2">
              <Tooltip>
                <TooltipTrigger class="cursor-default text-left">
                  <span class="text-sm">{{ formatRelative(entry.occurredAt) }}</span>
                </TooltipTrigger>
                <TooltipContent>{{ formatDate(entry.occurredAt) }}</TooltipContent>
              </Tooltip>
            </TableCell>

            <TableCell class="w-full max-w-0 py-2">
              <div class="flex items-baseline gap-2">
                <span class="text-sm font-medium whitespace-nowrap">
                  {{ humanizeAction(entry.action) }}
                </span>
                <span class="text-muted-foreground truncate font-mono text-xs">{{ entry.action }}</span>
              </div>
              <p
                v-if="metadata.length"
                class="text-muted-foreground truncate text-xs"
                :title="metadata.map(([k, v]) => `${k}: ${v}`).join('  ·  ')"
              >
                <span v-for="[key, value] in metadata" :key="key" class="mr-3 whitespace-nowrap">
                  {{ key }}: <span class="font-mono">{{ value }}</span>
                </span>
              </p>
            </TableCell>

            <TableCell class="py-2">
              <div class="flex items-center gap-1.5">
                <component
                  :is="actorIcons[entry.actorType] ?? UserIcon"
                  class="text-muted-foreground size-3.5 shrink-0"
                />
                <span class="truncate text-sm">{{ entry.actorDisplay ?? 'Unknown' }}</span>
              </div>
              <p v-if="entry.ipAddress" class="text-muted-foreground truncate font-mono text-[0.6875rem]">
                {{ entry.ipAddress }}
              </p>
            </TableCell>

            <TableCell class="py-2">
              <p v-if="entry.targetName" class="truncate text-sm">{{ entry.targetName }}</p>
              <p v-if="entry.targetType" class="text-muted-foreground truncate text-[0.6875rem]">
                {{ entry.targetType }}
              </p>
              <p v-if="!entry.targetName && !entry.targetType" class="text-muted-foreground text-sm">
                —
              </p>
            </TableCell>

            <TableCell v-if="showTenant" class="py-2">
              <span class="text-muted-foreground font-mono text-xs">
                {{ entry.tenantId ? entry.tenantId.slice(0, 8) : 'instance' }}
              </span>
            </TableCell>

            <TableCell class="py-2">
              <Badge
                :variant="entry.outcome === 'SUCCESS' ? 'secondary' : 'destructive'"
                class="text-xs"
              >
                {{ entry.outcome.toLowerCase() }}
              </Badge>
            </TableCell>
          </TableRow>
        </TableBody>
      </Table>
    </div>

    <div v-if="hasMore" class="flex justify-center">
      <Button variant="outline" size="sm" :disabled="loadingMore" @click="emit('loadMore')">
        {{ loadingMore ? 'Loading…' : 'Load more' }}
      </Button>
    </div>
  </div>
</template>
