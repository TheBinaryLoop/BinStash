<script setup lang="ts">
import { Bot, CircleSlash, User as UserIcon, Wrench } from '@lucide/vue'
import { computed } from 'vue'

import { Badge } from '@/components/ui/badge'
import type { AuditEntryFragment } from '@/graphql/generated'
import { actionSubject, humanizeAction, parseAuditMetadata } from '@/lib/audit'
import { formatDate, formatRelative } from '@/lib/format'

/**
 * The full record behind one audit row.
 *
 * The table can only ever show a truncated line, so this is where an entry becomes answerable:
 * what happened, to what, by whom, from where, exactly when, and every metadata field the
 * mutation recorded — laid out as a definition list so keys and values line up and can be
 * scanned vertically instead of read as a run-on sentence.
 */
const props = defineProps<{ entry: AuditEntryFragment; showTenant?: boolean }>()

const actorIcons = {
  USER: UserIcon,
  SERVICE_ACCOUNT: Bot,
  SYSTEM: Wrench,
  ANONYMOUS: CircleSlash,
} as const

const actorLabels = {
  USER: 'User',
  SERVICE_ACCOUNT: 'Service account',
  SYSTEM: 'System',
  ANONYMOUS: 'Anonymous',
} as const

/**
 * Fields are laid out one of two ways.
 *
 * A short single value reads best on the same line as its label, right-aligned so the values
 * form a scannable column. Anything longer has to stack: squeezed into half the card's width, a
 * value like `ChunkStoreGc:Schedule:Enabled` has nowhere to wrap except mid-token, and the
 * result is unreadable in exactly the case — a config change — where the keys are the point.
 */
const INLINE_VALUE_MAX = 24

const fields = computed(() =>
  parseAuditMetadata(props.entry.metadata).map((field) => ({
    ...field,
    stacked: field.values.length > 1 || (field.values[0]?.length ?? 0) > INLINE_VALUE_MAX,
  })),
)

const failed = computed(() => props.entry.outcome !== 'SUCCESS')
</script>

<template>
  <!-- Sized to the room the popover actually has rather than a fixed cap: an entry with many
       metadata fields otherwise gets clipped mid-row, and a clipped row in an audit trail reads
       as a missing value rather than as one that scrolled. -->
  <div
    class="divide-hairline flex max-h-(--reka-hover-card-content-available-height) flex-col divide-y overflow-hidden"
  >
    <!-- Header: what happened, and whether it worked. The outcome leads because a denied
         action and a successful one otherwise look identical at a glance. -->
    <header class="space-y-1.5 p-3">
      <div class="flex items-start justify-between gap-2">
        <div class="min-w-0">
          <p class="text-muted-foreground text-[0.6875rem] tracking-wide uppercase">
            {{ actionSubject(entry.action) }}
          </p>
          <p class="text-sm leading-snug font-medium">{{ humanizeAction(entry.action) }}</p>
        </div>
        <Badge :variant="failed ? 'destructive' : 'secondary'" class="shrink-0 text-[0.6875rem]">
          {{ entry.outcome.toLowerCase() }}
        </Badge>
      </div>
      <p class="text-muted-foreground font-mono text-[0.6875rem]">{{ entry.action }}</p>
    </header>

    <!-- Who and what. Two columns so the eye can pick one without reading the other. -->
    <div class="grid grid-cols-2 gap-3 p-3 text-xs">
      <div class="min-w-0 space-y-1">
        <p class="text-muted-foreground text-[0.6875rem] tracking-wide uppercase">Actor</p>
        <div class="flex items-center gap-1.5">
          <component
            :is="actorIcons[entry.actorType] ?? UserIcon"
            class="text-muted-foreground size-3.5 shrink-0"
          />
          <span class="truncate">{{ entry.actorDisplay ?? actorLabels[entry.actorType] ?? 'Unknown' }}</span>
        </div>
        <p class="text-muted-foreground text-[0.6875rem]">
          {{ actorLabels[entry.actorType] ?? entry.actorType }}
          <template v-if="entry.ipAddress">
            · <span class="font-mono">{{ entry.ipAddress }}</span>
          </template>
        </p>
      </div>

      <div class="min-w-0 space-y-1">
        <p class="text-muted-foreground text-[0.6875rem] tracking-wide uppercase">Target</p>
        <p class="truncate">{{ entry.targetName ?? entry.targetType ?? '—' }}</p>
        <p v-if="entry.targetName && entry.targetType" class="text-muted-foreground text-[0.6875rem]">
          {{ entry.targetType }}
        </p>
        <p v-if="entry.targetId" class="text-muted-foreground truncate font-mono text-[0.6875rem]">
          {{ entry.targetId }}
        </p>
      </div>
    </div>

    <!-- Metadata: the part that was previously a single unformatted line in a native tooltip. -->
    <div v-if="fields.length" class="min-h-0 flex-1 overflow-y-auto p-3">
      <p class="text-muted-foreground mb-2 text-[0.6875rem] tracking-wide uppercase">Details</p>
      <dl class="space-y-2">
        <div
          v-for="field in fields"
          :key="field.key"
          :class="field.stacked ? 'space-y-0.5' : 'flex items-baseline justify-between gap-3'"
        >
          <dt class="text-muted-foreground shrink-0 text-xs">{{ field.label }}</dt>
          <dd
            class="min-w-0 text-xs"
            :class="[field.mono ? 'font-mono' : '', field.stacked ? '' : 'text-right']"
          >
            <!-- List values stack rather than joining with commas: `changedKeys` is usually
                 several config paths, and one per line is the difference between reading them
                 and counting them. -->
            <span
              v-for="(value, index) in field.values"
              :key="index"
              class="block [overflow-wrap:anywhere]"
            >
              {{ value }}
            </span>
          </dd>
        </div>
      </dl>
    </div>

    <footer class="text-muted-foreground flex items-baseline justify-between gap-3 p-3 text-[0.6875rem]">
      <span>{{ formatDate(entry.occurredAt) }}</span>
      <span>{{ formatRelative(entry.occurredAt) }}</span>
    </footer>

    <p
      v-if="showTenant"
      class="text-muted-foreground px-3 pb-3 text-[0.6875rem]"
    >
      {{ entry.tenantId ? `Workspace ${entry.tenantId.slice(0, 8)}` : 'Instance-wide' }}
    </p>
  </div>
</template>
