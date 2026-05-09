<template>
  <header
    class="sticky top-0 before:absolute before:inset-0 before:backdrop-blur-md max-lg:before:bg-white/90 dark:max-lg:before:bg-gray-800/90 before:-z-10 z-30"
    :class="[
      variant === 'tenant' ? 'before:bg-transparent after:absolute after:h-px after:inset-x-0 after:top-full after:bg-slate-200 dark:after:bg-white/5 after:-z-10' : '',
      variant === 'v2' || variant === 'v3' ? 'before:bg-white after:absolute after:h-px after:inset-x-0 after:top-full after:bg-gray-200 dark:after:bg-gray-700/60 after:-z-10' : 'max-lg:shadow-xs lg:before:bg-gray-100/90 dark:lg:before:bg-gray-900/90',
      variant === 'v2' ? 'dark:before:bg-gray-800' : '',
      variant === 'v3' ? 'dark:before:bg-gray-900' : '',
    ]"
  >
    <div :class="variant === 'tenant' ? 'px-5 sm:px-6 lg:px-8' : 'px-4 sm:px-6 lg:px-8'">
      <div
        class="flex items-center justify-between h-16"
        :class="variant === 'v2' || variant === 'v3' || variant === 'tenant' ? '' : 'lg:border-b border-gray-200 dark:border-gray-700/60'"
      >

        <!-- Header: Left side -->
        <div class="flex">

          <!-- Hamburger button -->
          <button class="text-gray-500 hover:text-gray-600 dark:hover:text-gray-400 lg:hidden" @click.stop="$emit('toggle-sidebar')" aria-controls="sidebar" :aria-expanded="sidebarOpen">
            <span class="sr-only">Open sidebar</span>
            <svg class="w-6 h-6 fill-current" viewBox="0 0 24 24" xmlns="http://www.w3.org/2000/svg">
              <rect x="4" y="5" width="16" height="2" />
              <rect x="4" y="11" width="16" height="2" />
              <rect x="4" y="17" width="16" height="2" />
            </svg>
          </button>

        </div>

        <!-- Header: Right side -->
        <div class="flex items-center space-x-3">
          <div>
            <button
              class="w-8 h-8 flex items-center justify-center rounded-full ml-3"
              :class="[
                variant === 'tenant'
                  ? 'hover:bg-slate-100 dark:hover:bg-white/5'
                  : 'hover:bg-gray-100 lg:hover:bg-gray-200 dark:hover:bg-gray-700/50 dark:lg:hover:bg-gray-800',
                searchModalOpen
                  ? (variant === 'tenant' ? 'bg-slate-100 dark:bg-white/5' : 'bg-gray-200 dark:bg-gray-800')
                  : ''
              ]"
              @click.stop="openSearchModal"
              aria-controls="search-modal"
            >
              <span class="sr-only">Search</span>
              <svg class="fill-current text-gray-500/80 dark:text-gray-400/80" width="16" height="16" viewBox="0 0 16 16" xmlns="http://www.w3.org/2000/svg">
                  <path d="M7 14c-3.86 0-7-3.14-7-7s3.14-7 7-7 7 3.14 7 7-3.14 7-7 7ZM7 2C4.243 2 2 4.243 2 7s2.243 5 5 5 5-2.243 5-5-2.243-5-5-5Z" />
                  <path d="m13.314 11.9 2.393 2.393a.999.999 0 1 1-1.414 1.414L11.9 13.314a8.019 8.019 0 0 0 1.414-1.414Z" />
              </svg>
            </button>          
          </div>
          <Notifications align="right" />
          <Help align="right" />
          <ThemeToggle />
          <!-- Divider -->
          <hr class="w-px h-6 border-none" :class="variant === 'tenant' ? 'bg-slate-200 dark:bg-white/5' : 'bg-gray-200 dark:bg-gray-700/60'" />
          <UserMenu align="right" />

        </div>

      </div>
    </div>
  </header>
</template>

<script>
import { useSearchModal } from '@/composables/useSearchModal'

import Notifications from '@/components/DropdownNotifications.vue'
import Help from '@/components/DropdownHelp.vue'
import ThemeToggle from '@/components/ThemeToggle.vue'
import UserMenu from '@/components/DropdownProfile.vue'

export default {
  name: 'Header',
  props: [
    'sidebarOpen',
    'variant',
  ],
  components: {
    Notifications,
    Help,
    ThemeToggle,
    UserMenu,
  },
  setup() {
    const { searchModalOpen, openSearchModal } = useSearchModal()
    return {
      searchModalOpen,
      openSearchModal,
    }  
  }  
}
</script>