<template>
  <div class="flex h-dvh overflow-hidden">

    <!-- Sidebar -->
    <TenantSidebar
      v-if="showTenantSidebar"
      :sidebarOpen="sidebarOpen"
      @close-sidebar="sidebarOpen = false"
      variant="v2"
    />
    <InstanceSidebar
      v-else-if="showInstanceAdminSidebar"
      :sidebarOpen="sidebarOpen"
      @close-sidebar="sidebarOpen = false"
      variant="v2"
    />

    <!-- Content area -->
    <div class="relative flex flex-col flex-1 overflow-y-auto overflow-x-hidden bg-white dark:bg-gray-900">
      
      <!-- Site header -->
      <Header :sidebarOpen="sidebarOpen" @toggle-sidebar="sidebarOpen = !sidebarOpen" variant="v3" />

      <main class="grow">
        <div class="px-4 sm:px-6 lg:px-8 py-8 w-full max-w-384 mx-auto">

          <div class="max-w-2xl m-auto mt-16">

            <div class="text-center px-4">
              <div class="inline-flex mb-8">
                <img class="dark:hidden" src="../../images//404-illustration.svg" width="176" height="176" alt="404 illustration" />
                <img class="hidden dark:block" src="../../images//404-illustration-dark.svg" width="176" height="176" alt="404 illustration dark" />                
              </div>
              <div class="mb-6">Hmm...this page doesn't exist. Try searching for something else!</div>
              <router-link to="/" class="btn bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white">Back To Dashboard</router-link>
            </div>

          </div>

        </div>        
      </main>

    </div> 

  </div>
</template>

<script>
import { ref, computed } from 'vue'
import InstanceSidebar from '@/features/instance/components/InstanceSidebar.vue'
import TenantSidebar from '@/features/tenants/components/TenantSidebar.vue'
import Header from '@/shared/components/navigation/Header.vue'
import { useTenantStore } from '../../stores/tenant'
import { useAuthStore } from '../../stores/auth'
import { useRoute } from 'vue-router'

export default {
  name: 'PageNotFound',
  components: {
    InstanceSidebar,
    TenantSidebar,
    Header,
  },
  setup() {
    const sidebarOpen = ref(false)
    const authStore = useAuthStore()
    const route = useRoute()

    const isInstanceAdmin = computed(() =>
      (authStore.user?.roles ?? []).includes('InstanceAdmin')
    )

    const showTenantSidebar = computed(() => !!route.params.tenantId)

    const showInstanceAdminSidebar = computed(() =>
      !showTenantSidebar.value && isInstanceAdmin.value
    )

    return {
      sidebarOpen,
      showTenantSidebar,
      showInstanceAdminSidebar,
    }
  }
}
</script>