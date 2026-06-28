import { defineStore } from 'pinia';
import { useTenantSettingsStore } from './tenantSettings';

export type TenantSummaryDto = {
    tenantId: string
    name: string
    slug?: string
    role: string
}

const LS_KEY = 'binstash.tenantId';

export const useTenantStore = defineStore('tenant', {
    state: () => ({
        tenants: [] as TenantSummaryDto[],
        currentTenantId: (localStorage.getItem(LS_KEY) ?? null) as string | null,
        isLoading: false,
        isLoaded: false,
    }),
    getters: {
        currentTenant: (s) => s.tenants.find(t => t.tenantId === s.currentTenantId) ?? null,
        hasTenants: (s) => s.tenants.length > 0,
    },
    actions: {
        setTenants(tenants: TenantSummaryDto[]) {
            this.tenants = tenants;
            this.isLoaded = true;
        },
        upsertTenant(tenant: TenantSummaryDto) {
            const idx = this.tenants.findIndex(t => t.tenantId === tenant.tenantId);
            if (idx >= 0) {
                this.tenants[idx] = tenant;
            } else {
                this.tenants = [...this.tenants, tenant];
            }
            this.isLoaded = true;
        },
        setCurrentTenant(id: string | null) {
            this.currentTenantId = id;
            if (id) 
                localStorage.setItem(LS_KEY, id);
            else
                localStorage.removeItem(LS_KEY);
        },
        clear() {
            this.tenants = [];
            this.setCurrentTenant(null);
            this.isLoaded = false;
        },
    },
})
