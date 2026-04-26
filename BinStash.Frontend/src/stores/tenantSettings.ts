import { defineStore } from "pinia";
import { ref, watch } from "vue";
import { useTenantStore } from "./tenant";
import { StorageClassDto, listStorageClasses } from "../api/tenants";

export const useTenantSettingsStore = defineStore("tenantSettings", () => {
    const tenantStore = useTenantStore();

    const loading = ref(false);
    const allowedStorageClasses = ref<StorageClassDto[] | null>(null);

    async function loadSettings() {
        console.log("Loading tenant settings...");
        loading.value = true;
        try {
            allowedStorageClasses.value = await listStorageClasses();
        } finally {
            loading.value = false;
        }
    }

    function clear() {
        allowedStorageClasses.value = null;
    }

    watch(() => tenantStore.currentTenantId, (newTenantId, oldTenantId) => {
            console.log("Tenant changed from", oldTenantId, "to", newTenantId);
            if (newTenantId) {
                loadSettings();
            } else {
                clear();
            }
        },
        { immediate: true }
    );

    return {
        allowedStorageClasses,
        loading,
        loadSettings,
        clear,
    };
});