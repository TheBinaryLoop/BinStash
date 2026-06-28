<template>
  <div
    ref="triggerWrapper"
    class="relative inline-flex"
    @mouseenter="openTooltip"
    @mouseleave="closeTooltip"
    @focusin="openTooltip"
    @focusout="closeTooltip"
  >
    <button
      ref="triggerButton"
      class="block"
      aria-haspopup="true"
      :aria-expanded="tooltipOpen"
      @click.prevent
    >
      <svg class="fill-current text-gray-400 dark:text-gray-500" width="16" height="16" viewBox="0 0 16 16">
        <path d="M8 0C3.6 0 0 3.6 0 8s3.6 8 8 8 8-3.6 8-8-3.6-8-8-8zm0 12c-.6 0-1-.4-1-1s.4-1 1-1 1 .4 1 1-.4 1-1 1zm1-3H7V4h2v5z" />
      </svg>
    </button>
    <Teleport to="body">
      <div
        v-if="tooltipOpen"
        class="fixed z-[120] pointer-events-none"
        :style="tooltipStyle"
      >
        <transition
          enter-active-class="transition ease-out duration-200 transform"
          enter-from-class="opacity-0 -translate-y-2"
          enter-to-class="opacity-100 translate-y-0"
          leave-active-class="transition ease-out duration-200"
          leave-from-class="opacity-100"
          leave-to-class="opacity-0"
        >
          <div
            class="rounded-lg border overflow-hidden shadow-lg"
            :class="[
              colorClasses(bg),
              sizeClasses(size),
            ]"
          >
            <slot />
          </div>
        </transition>
      </div>
    </Teleport>
  </div>
</template>

<script>
import { computed, onBeforeUnmount, onMounted, ref } from 'vue'

export default {
  name: 'Tooltip',
  props: ['bg', 'size', 'position'],
  setup(props) {

    const tooltipOpen = ref(false)
    const triggerButton = ref(null)
    const triggerWrapper = ref(null)
    const coordinates = ref({ top: 0, left: 0 })

    const updatePosition = () => {
      const el = triggerButton.value || triggerWrapper.value
      if (!el) return

      const rect = el.getBoundingClientRect()
      const gap = 10

      switch (props.position) {
        case 'right':
          coordinates.value = {
            top: rect.top + rect.height / 2,
            left: rect.right + gap,
          }
          break
        case 'left':
          coordinates.value = {
            top: rect.top + rect.height / 2,
            left: rect.left - gap,
          }
          break
        case 'bottom':
          coordinates.value = {
            top: rect.bottom + gap,
            left: rect.left + rect.width / 2,
          }
          break
        default:
          coordinates.value = {
            top: rect.top - gap,
            left: rect.left + rect.width / 2,
          }
          break
      }
    }

    const openTooltip = () => {
      updatePosition()
      tooltipOpen.value = true
    }

    const closeTooltip = () => {
      tooltipOpen.value = false
    }

    const tooltipStyle = computed(() => {
      switch (props.position) {
        case 'right':
          return {
            top: `${coordinates.value.top}px`,
            left: `${coordinates.value.left}px`,
            transform: 'translateY(-50%)',
          }
        case 'left':
          return {
            top: `${coordinates.value.top}px`,
            left: `${coordinates.value.left}px`,
            transform: 'translate(-100%, -50%)',
          }
        case 'bottom':
          return {
            top: `${coordinates.value.top}px`,
            left: `${coordinates.value.left}px`,
            transform: 'translateX(-50%)',
          }
        default:
          return {
            top: `${coordinates.value.top}px`,
            left: `${coordinates.value.left}px`,
            transform: 'translate(-50%, -100%)',
          }
      }
    })

    const handleViewportChange = () => {
      if (tooltipOpen.value) updatePosition()
    }

    onMounted(() => {
      window.addEventListener('scroll', handleViewportChange, true)
      window.addEventListener('resize', handleViewportChange)
    })

    onBeforeUnmount(() => {
      window.removeEventListener('scroll', handleViewportChange, true)
      window.removeEventListener('resize', handleViewportChange)
    })

    const sizeClasses = (size) => {
      switch (size) {
        case 'lg':
          return 'min-w-72 px-3 py-2';
        case 'md':
          return 'min-w-56 px-3 py-2';
        case 'sm':
          return 'min-w-44 px-3 py-2';
        default:
          return 'px-3 py-2';
      }
    }

    const colorClasses = (bg) => {
      switch (bg) {
        case 'light':
          return 'bg-white text-gray-600 border-gray-200'
        case 'dark':
          return 'bg-gray-800 text-gray-100 border-gray-700/60'
        default:
          return 'text-gray-600 bg-white dark:bg-gray-800 dark:text-gray-100 border-gray-200 dark:border-gray-700/60'
      }
    }      

    return {
      tooltipOpen,
      triggerButton,
      triggerWrapper,
      tooltipStyle,
      openTooltip,
      closeTooltip,
      sizeClasses,
      colorClasses,
    }
  }
}
</script>