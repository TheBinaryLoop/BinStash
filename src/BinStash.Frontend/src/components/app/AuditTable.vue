<script setup lang="ts">
import { Bot, CircleSlash, Info, TriangleAlert, User as UserIcon, Wrench } from '@lucide/vue'
import { computed } from 'vue'

import AuditEntryDetails from '@/components/app/AuditEntryDetails.vue'
import EmptyState from '@/components/app/EmptyState.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { HoverCard, HoverCardContent, HoverCardTrigger } from '@/components/ui/hover-card'
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
import { humanizeAction, parseAuditMetadata, summarizeMetadata } from '@/lib/audit'
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

const rows = computed(() =>
  props.entries.map((entry) => {
    const fields = parseAuditMetadata(entry.metadata)
    return { entry, fields, summary: summarizeMetadata(fields) }
  }),
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
            <TableHead v-if="showTenant" class="w-[10rem]">Workspace</TableHead>
            <TableHead class="w-[7rem]">Outcome</TableHead>
          </TableRow>
        </TableHeader>

        <TableBody>
          <TableRow v-for="{ entry, fields, summary } in rows" :key="entry.id" class="align-middle">
            <TableCell class="py-2">
              <Tooltip>
                <TooltipTrigger class="cursor-default text-left">
                  <span class="text-sm">{{ formatRelative(entry.occurredAt) }}</span>
                </TooltipTrigger>
                <TooltipContent>{{ formatDate(entry.occurredAt) }}</TooltipContent>
              </Tooltip>
            </TableCell>

            <TableCell class="w-full max-w-0 py-2">
              <!-- The whole cell is the trigger, and it is a button so the detail is reachable
                   by keyboard and on touch — the native `title` it replaces was neither. -->
              <HoverCard>
                <HoverCardTrigger as-child>
                  <button
                    type="button"
                    class="group focus-visible:ring-ring/50 block w-full cursor-default rounded-sm text-left focus-visible:ring-2 focus-visible:outline-none"
                  >
                    <span class="flex items-baseline gap-1.5">
                      <span class="truncate text-sm font-medium">
                        {{ humanizeAction(entry.action) }}
                      </span>
                      <Info
                        class="text-muted-foreground/0 group-hover:text-muted-foreground group-focus-visible:text-muted-foreground size-3 shrink-0 transition-colors"
                        aria-hidden="true"
                      />
                    </span>
                    <span
                      v-if="summary"
                      class="text-muted-foreground block truncate text-xs"
                    >
                      {{ summary }}
                    </span>
                    <span v-else class="text-muted-foreground block truncate font-mono text-xs">
                      {{ entry.action }}
                    </span>
                    <span class="sr-only">Show full audit entry</span>
                  </button>
                </HoverCardTrigger>

                <HoverCardContent :class="fields.length > 6 ? 'w-96' : 'w-80'">
                  <AuditEntryDetails :entry="entry" :show-tenant="showTenant" />
                </HoverCardContent>
              </HoverCard>
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
