<script setup lang="ts">
import { computed, ref, watch } from 'vue'

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
import { Switch } from '@/components/ui/switch'

/**
 * Starts an online collection run.
 *
 * Not a plain confirm dialog because the three modes differ in whether anything is destroyed,
 * and that difference has to be visible *before* the button is pressed rather than explained
 * afterwards. The summary line restates the chosen mode in those terms.
 */
const open = defineModel<boolean>('open', { default: false })

const props = defineProps<{
  storeName: string
  /** Instance-wide quarantine retention, so the dialog can say when bytes actually go. */
  retentionHours?: number | null
  busy?: boolean
}>()

const emit = defineEmits<{ confirm: [{ dryRun: boolean; skipReclaim: boolean }] }>()

const dryRun = ref(true)
const skipReclaim = ref(false)

// Reopening should not silently inherit the last run's settings — the safe mode is the default
// every time, and a destructive run should be a fresh decision.
watch(open, (isOpen) => {
  if (isOpen) {
    dryRun.value = true
    skipReclaim.value = false
  }
})

// Reporting-only already implies destroying nothing, so the weaker option is redundant then.
watch(dryRun, (value) => {
  if (value) skipReclaim.value = false
})

const retentionLabel = computed(() =>
  props.retentionHours == null
    ? 'the configured retention window'
    : props.retentionHours >= 48
      ? `${Math.round(props.retentionHours / 24)} days`
      : `${Math.round(props.retentionHours)} hours`,
)

const summary = computed(() => {
  if (dryRun.value) {
    return 'Reports what is unreachable and changes nothing on disk.'
  }
  if (skipReclaim.value) {
    return 'Hides unreachable content from deduplication but destroys nothing. Every byte stays recoverable until a later run reclaims it.'
  }
  return `Hides unreachable content now, and destroys content quarantined more than ${retentionLabel.value} ago. That part cannot be undone.`
})

const destructive = computed(() => !dryRun.value && !skipReclaim.value)
</script>

<template>
  <Dialog v-model:open="open">
    <DialogContent class="sm:max-w-lg">
      <DialogHeader>
        <DialogTitle>Collect garbage on {{ storeName }}?</DialogTitle>
        <DialogDescription>
          Finds content no release refers to any more — orphans from uploads that never finished —
          and reclaims the space. The store stays online: uploads and downloads keep working
          throughout.
        </DialogDescription>
      </DialogHeader>

      <div class="space-y-4 py-2">
        <div class="flex items-start justify-between gap-4">
          <div class="space-y-0.5">
            <Label for="gc-dry-run">Report only</Label>
            <p class="text-muted-foreground text-xs">
              Measure what would be collected without touching anything.
            </p>
          </div>
          <Switch id="gc-dry-run" v-model="dryRun" />
        </div>

        <div class="flex items-start justify-between gap-4" :class="dryRun ? 'opacity-50' : ''">
          <div class="space-y-0.5">
            <Label for="gc-skip-reclaim">Quarantine only</Label>
            <p class="text-muted-foreground text-xs">
              Stop short of destroying anything, so the result can be inspected first.
            </p>
          </div>
          <Switch id="gc-skip-reclaim" v-model="skipReclaim" :disabled="dryRun" />
        </div>

        <p
          class="rounded-md p-3 text-xs"
          :class="destructive ? 'bg-destructive/10 text-destructive' : 'bg-muted text-muted-foreground'"
        >
          {{ summary }}
        </p>
      </div>

      <DialogFooter>
        <Button variant="outline" :disabled="busy" @click="open = false">Cancel</Button>
        <Button
          :variant="destructive ? 'destructive' : 'default'"
          :disabled="busy"
          @click="emit('confirm', { dryRun, skipReclaim })"
        >
          {{ dryRun ? 'Run report' : 'Start collection' }}
        </Button>
      </DialogFooter>
    </DialogContent>
  </Dialog>
</template>
