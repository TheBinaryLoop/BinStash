import { ref } from 'vue'

const searchModalOpen = ref(false)

export function useSearchModal() {
  function openSearchModal() {
    searchModalOpen.value = true
  }

  function closeSearchModal() {
    searchModalOpen.value = false
  }

  return {
    searchModalOpen,
    openSearchModal,
    closeSearchModal,
  }
}