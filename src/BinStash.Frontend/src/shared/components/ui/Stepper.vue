<template>
  <ol class="flex items-center">
    <li
      v-for="(step, i) in normalized"
      :key="i"
      class="flex items-center"
      :class="i < normalized.length - 1 ? 'flex-1' : ''"
    >
      <div class="flex flex-col items-center gap-1.5">
        <button
          type="button"
          :disabled="!clickable || i > current"
          class="flex h-7 w-7 shrink-0 items-center justify-center rounded-full text-xs font-semibold transition"
          :class="i < current
            ? 'bg-accent text-white'
            : i === current
              ? 'bg-accent-soft text-accent ring-2 ring-accent'
              : 'border border-hairline bg-card text-ink-subtle'"
          @click="clickable && i <= current && emit('select', i)"
        >
          <IconCheck v-if="i < current" class="h-4 w-4" />
          <span v-else>{{ i + 1 }}</span>
        </button>
        <span
          v-if="step.label && showLabels"
          class="hidden max-w-24 truncate text-center text-xs sm:block"
          :class="i === current ? 'font-medium text-ink-strong' : 'text-ink-subtle'"
        >{{ step.label }}</span>
      </div>
      <div
        v-if="i < normalized.length - 1"
        class="mx-2 h-px flex-1 transition-colors"
        :class="i < current ? 'bg-accent' : 'bg-hairline'"
      />
    </li>
  </ol>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { IconCheck } from '@tabler/icons-vue'

const props = withDefaults(defineProps<{
  steps: (string | { label: string })[]
  current: number
  clickable?: boolean
  showLabels?: boolean
}>(), {
  clickable: false,
  showLabels: true,
})

const emit = defineEmits<{ (e: 'select', index: number): void }>()

const normalized = computed(() =>
  props.steps.map((s) => (typeof s === 'string' ? { label: s } : s)),
)
</script>
