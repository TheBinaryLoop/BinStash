<template>
  <header class="sticky top-0 z-30 border-b border-hairline bg-canvas/80 backdrop-blur-md">
    <div class="flex h-14 items-center justify-between gap-3 px-4 sm:px-6 lg:px-8">
      <!-- Left: hamburger (mobile) + search -->
      <div class="flex flex-1 items-center gap-3">
        <button
          class="text-ink-muted transition hover:text-ink-strong lg:hidden"
          aria-controls="sidebar"
          :aria-expanded="sidebarOpen"
          @click.stop="emit('toggle-sidebar')"
        >
          <span class="sr-only">Open sidebar</span>
          <IconMenu2 class="h-6 w-6" />
        </button>

        <!-- Desktop search trigger -->
        <button
          class="hidden h-9 w-full max-w-xs items-center gap-2 rounded-control border border-hairline bg-card px-3 text-sm text-ink-subtle transition hover:border-ink-subtle/50 sm:flex"
          @click.stop="openSearchModal"
        >
          <IconSearch class="h-4 w-4 shrink-0" />
          <span class="flex-1 text-left">Search…</span>
          <kbd class="rounded border border-hairline px-1.5 py-0.5 text-[10px] font-medium text-ink-subtle">⌘K</kbd>
        </button>
      </div>

      <!-- Right -->
      <div class="flex items-center gap-1.5">
        <button
          class="flex h-9 w-9 items-center justify-center rounded-control text-ink-muted transition hover:bg-raised hover:text-ink-strong sm:hidden"
          @click.stop="openSearchModal"
        >
          <span class="sr-only">Search</span>
          <IconSearch class="h-5 w-5" />
        </button>
        <ThemeToggle />
        <hr class="mx-1 h-6 w-px border-none bg-hairline" />
        <UserMenu align="right" />
      </div>
    </div>
  </header>
</template>

<script setup lang="ts">
import { IconMenu2, IconSearch } from '@tabler/icons-vue'
import { useSearchModal } from '@/composables/useSearchModal'
import ThemeToggle from '@/components/ThemeToggle.vue'
import UserMenu from '@/components/DropdownProfile.vue'

defineProps<{ sidebarOpen?: boolean; variant?: string }>()
const emit = defineEmits<{ (e: 'toggle-sidebar'): void }>()

const { openSearchModal } = useSearchModal()
</script>
