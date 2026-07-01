<template>
  <Teleport to="body">
    <Transition
      enter-active-class="transition duration-150 ease-out"
      enter-from-class="opacity-0"
      enter-to-class="opacity-100"
      leave-active-class="transition duration-100 ease-in"
      leave-from-class="opacity-100"
      leave-to-class="opacity-0"
    >
      <div v-if="open" class="fixed inset-0 z-50 flex items-start justify-center overflow-y-auto p-4 sm:p-6">
        <!-- Backdrop -->
        <div
          class="fixed inset-0 bg-slate-950/60 backdrop-blur-sm"
          @click="closeOnBackdrop && close()"
        />

        <!-- Panel -->
        <Transition
          enter-active-class="transition duration-150 ease-out"
          enter-from-class="opacity-0 translate-y-2 sm:scale-95"
          enter-to-class="opacity-100 translate-y-0 sm:scale-100"
          leave-active-class="transition duration-100 ease-in"
          leave-from-class="opacity-100 translate-y-0 sm:scale-100"
          leave-to-class="opacity-0 translate-y-2 sm:scale-95"
        >
          <div
            v-if="open"
            ref="panel"
            role="dialog"
            aria-modal="true"
            tabindex="-1"
            :class="['relative my-auto w-full rounded-panel border border-hairline bg-panel shadow-2xl focus:outline-none', sizeClass]"
          >
            <!-- Header -->
            <div v-if="title || $slots.header" class="flex items-start justify-between gap-4 border-b border-hairline px-5 py-4 sm:px-6">
              <div class="min-w-0">
                <slot name="header">
                  <h2 class="text-base font-semibold text-ink-strong">{{ title }}</h2>
                  <p v-if="description" class="mt-1 text-sm text-ink-muted">{{ description }}</p>
                </slot>
              </div>
              <button
                type="button"
                class="-mr-1 -mt-1 flex h-8 w-8 shrink-0 items-center justify-center rounded-control text-ink-subtle transition hover:bg-raised hover:text-ink-strong"
                aria-label="Close"
                @click="close()"
              >
                <IconX class="h-5 w-5" />
              </button>
            </div>

            <!-- Body -->
            <div class="px-5 py-5 sm:px-6">
              <slot />
            </div>

            <!-- Footer -->
            <div v-if="$slots.footer" class="flex items-center justify-end gap-2 border-t border-hairline px-5 py-4 sm:px-6">
              <slot name="footer" />
            </div>
          </div>
        </Transition>
      </div>
    </Transition>
  </Teleport>
</template>

<script setup lang="ts">
import { computed, ref, watch, onUnmounted, nextTick } from 'vue'
import { IconX } from '@tabler/icons-vue'

const open = defineModel<boolean>('open', { default: false })

const props = withDefaults(defineProps<{
  title?: string
  description?: string
  size?: 'sm' | 'md' | 'lg' | 'xl'
  closeOnBackdrop?: boolean
}>(), {
  size: 'md',
  closeOnBackdrop: true,
})

const emit = defineEmits<{ (e: 'close'): void }>()

const panel = ref<HTMLElement | null>(null)

const sizeClass = computed(() => ({
  sm: 'max-w-sm',
  md: 'max-w-lg',
  lg: 'max-w-2xl',
  xl: 'max-w-4xl',
}[props.size]))

function close() {
  open.value = false
  emit('close')
}

function onKeydown(e: KeyboardEvent) {
  if (e.key === 'Escape') close()
}

watch(open, async (isOpen) => {
  if (isOpen) {
    document.addEventListener('keydown', onKeydown)
    document.body.style.overflow = 'hidden'
    await nextTick()
    panel.value?.focus()
  } else {
    document.removeEventListener('keydown', onKeydown)
    document.body.style.overflow = ''
  }
}, { immediate: true })

onUnmounted(() => {
  document.removeEventListener('keydown', onKeydown)
  document.body.style.overflow = ''
})
</script>
