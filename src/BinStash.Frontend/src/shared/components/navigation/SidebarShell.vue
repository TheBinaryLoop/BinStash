<template>
  <div class="min-w-fit">
    <!-- Backdrop (mobile) -->
    <div
      class="fixed inset-0 z-40 bg-slate-950/60 backdrop-blur-sm transition-opacity duration-200 lg:hidden"
      :class="sidebarOpen ? 'opacity-100' : 'pointer-events-none opacity-0'"
      aria-hidden="true"
    />

    <!-- Sidebar -->
    <div
      id="sidebar"
      ref="sidebar"
      class="absolute left-0 top-0 z-40 flex h-dvh shrink-0 flex-col overflow-y-auto border-r border-hairline bg-canvas text-ink-strong no-scrollbar transition-all duration-200 ease-in-out lg:static lg:left-auto lg:top-auto lg:translate-x-0 lg:flex!"
      :class="[
        sidebarOpen ? 'translate-x-0' : '-translate-x-64',
        'w-[224px] lg:w-[76px] lg:sidebar-expanded:w-[224px]!',
      ]"
    >
      <!-- Top: logo + mobile close -->
      <div class="flex items-center gap-2 px-4 py-5 lg:justify-center lg:px-0 lg:sidebar-expanded:justify-start lg:sidebar-expanded:px-4">
        <button
          ref="trigger"
          class="mr-1 text-ink-subtle transition hover:text-ink-strong lg:hidden"
          aria-controls="sidebar"
          :aria-expanded="sidebarOpen"
          @click.stop="emit('close-sidebar')"
        >
          <span class="sr-only">Close sidebar</span>
          <IconX class="h-6 w-6" />
        </button>

        <router-link :to="homeTo" class="flex shrink-0 items-center gap-2.5">
          <span class="flex h-8 w-8 items-center justify-center rounded-[10px] bg-linear-to-br from-brand-from to-brand-to shadow-sm">
            <svg class="h-5 w-5 fill-white" xmlns="http://www.w3.org/2000/svg" viewBox="0 0 32 32">
              <path d="M31.956 14.8C31.372 6.92 25.08.628 17.2.044V5.76a9.04 9.04 0 0 0 9.04 9.04h5.716ZM14.8 26.24v5.716C6.92 31.372.63 25.08.044 17.2H5.76a9.04 9.04 0 0 1 9.04 9.04Zm11.44-9.04h5.716c-.584 7.88-6.876 14.172-14.756 14.756V26.24a9.04 9.04 0 0 1 9.04-9.04ZM.044 14.8C.63 6.92 6.92.628 14.8.044V5.76a9.04 9.04 0 0 1-9.04 9.04H.044Z" />
            </svg>
          </span>
          <span class="text-sm font-semibold tracking-tight text-ink-strong lg:hidden lg:sidebar-expanded:inline">BinStash</span>
        </router-link>
      </div>

      <!-- Badge -->
      <div v-if="$slots.badge" class="mb-5 overflow-hidden px-3 lg:px-0 lg:sidebar-expanded:px-3">
        <slot name="badge" />
      </div>

      <!-- Navigation -->
      <nav class="flex-1 space-y-6 px-3 lg:px-2 lg:sidebar-expanded:px-3">
        <slot />
      </nav>

      <!-- Bottom -->
      <div class="mt-auto space-y-2 px-3 pb-5 lg:px-2 lg:sidebar-expanded:px-3">
        <div v-if="$slots.bottom" class="border-t border-hairline pt-3">
          <slot name="bottom" />
        </div>
        <button
          class="hidden w-full items-center justify-center rounded-control py-2 text-ink-subtle transition hover:bg-raised hover:text-ink-strong lg:flex"
          @click.prevent="sidebarExpanded = !sidebarExpanded"
        >
          <span class="sr-only">{{ sidebarExpanded ? 'Collapse sidebar' : 'Expand sidebar' }}</span>
          <IconChevronsLeft v-if="sidebarExpanded" class="h-5 w-5" />
          <IconChevronsRight v-else class="h-5 w-5" />
        </button>
      </div>
    </div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted, onUnmounted, watch } from 'vue'
import { IconX, IconChevronsLeft, IconChevronsRight } from '@tabler/icons-vue'
import { sidebarExpandedState, setSidebarExpanded } from '@/utils/sidebarExpanded'

const props = defineProps<{
  sidebarOpen: boolean
  homeTo: string | object
}>()

const emit = defineEmits<{ (e: 'close-sidebar'): void }>()

const trigger = ref<HTMLElement | null>(null)
const sidebar = ref<HTMLElement | null>(null)
const sidebarExpanded = sidebarExpandedState

function clickHandler({ target }: MouseEvent) {
  if (!sidebar.value || !trigger.value) return
  if (!props.sidebarOpen || sidebar.value.contains(target as Node) || trigger.value.contains(target as Node)) return
  emit('close-sidebar')
}

function keyHandler({ keyCode }: KeyboardEvent) {
  if (!props.sidebarOpen || keyCode !== 27) return
  emit('close-sidebar')
}

onMounted(() => {
  document.addEventListener('click', clickHandler)
  document.addEventListener('keydown', keyHandler)
  setSidebarExpanded(sidebarExpanded.value)
})

onUnmounted(() => {
  document.removeEventListener('click', clickHandler)
  document.removeEventListener('keydown', keyHandler)
})

watch(sidebarExpanded, () => setSidebarExpanded(sidebarExpanded.value))
</script>
