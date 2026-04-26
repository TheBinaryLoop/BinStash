<template>
  <router-view />
  <ModalSearch
    v-if="searchEnabled"
    id="global-search-modal"
    searchId="global-search"
    :modalOpen="searchModalOpen"
    @open-modal="openSearchModal"
    @close-modal="closeSearchModal"
  />
</template>

<script setup lang="ts">
import { computed, watch } from 'vue'
import { useAuthStore } from '@/stores/auth'
import { useGlobalKeybinds } from '@/composables/useGlobalKeybinds'
import { useSearchModal } from '@/composables/useSearchModal'
import { useGlobalSearchConfig } from '@/composables/useGlobalSearchConfig'
import ModalSearch from '@/components/ModalSearch.vue'
import '@/charts/ChartjsConfig'

const auth = useAuthStore()
const { searchModalOpen, openSearchModal, closeSearchModal } = useSearchModal()
const { isGlobalSearchEnabled } = useGlobalSearchConfig()

const searchEnabled = computed(() => isGlobalSearchEnabled({ isAuthenticated: auth.isAuthenticated }))

useGlobalKeybinds({
  onOpenSearch: openSearchModal,
  isEnabled: () => searchEnabled.value,
})

watch(searchEnabled, (enabled) => {
  if (!enabled) {
    closeSearchModal()
  }
})
</script>

