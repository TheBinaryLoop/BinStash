<script setup lang="ts">
import { Check, CircleCheck, KeyRound } from '@lucide/vue'
import { computed, onMounted, ref } from 'vue'
import { useRouter } from 'vue-router'

import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import {
  Select,
  SelectContent,
  SelectItem,
  SelectTrigger,
  SelectValue,
} from '@/components/ui/select'
import { Skeleton } from '@/components/ui/skeleton'
import { errorMessage } from '@/lib/errors'
import { DEFAULT_STORAGE_CLASSES, setupApi } from '@/lib/setup'
import type { SetupStatus, SetupStep } from '@/lib/setup'
import { markSetupComplete } from '@/router/guards'

/**
 * The server owns the wizard's position — `status.currentStep` is authoritative and
 * survives a reload or a browser change, so this component renders whatever step the
 * server says is next rather than tracking its own index.
 */
const router = useRouter()

const status = ref<SetupStatus | null>(null)
const loading = ref(true)
const busy = ref(false)
const error = ref('')

const step = computed<SetupStep>(() => status.value?.currentStep ?? 'Claim')
const isMulti = computed(() => status.value?.data?.tenancyMode !== 'Single')

const STEPS: Array<{ key: SetupStep; label: string }> = [
  { key: 'Claim', label: 'Claim' },
  { key: 'Tenancy', label: 'Tenancy' },
  { key: 'DefaultTenant', label: 'Workspace' },
  { key: 'ChunkStore', label: 'Chunk store' },
  { key: 'StorageClass', label: 'Storage' },
  { key: 'StorageClassDefaultMappings', label: 'Defaults' },
  { key: 'InstanceAdmin', label: 'Admin' },
  { key: 'Review', label: 'Finish' },
]

// The DefaultTenant step only exists in single-tenant mode.
const visibleSteps = computed(() =>
  STEPS.filter((entry) => entry.key !== 'DefaultTenant' || !isMulti.value),
)
const currentIndex = computed(() =>
  Math.max(
    visibleSteps.value.findIndex((entry) => entry.key === step.value),
    0,
  ),
)

async function refresh() {
  status.value = await setupApi.status()
}

onMounted(async () => {
  try {
    await refresh()
    if (status.value?.isInitialized) {
      markSetupComplete()
      await router.replace({ name: 'sign-in' })
    }
  } catch (caught) {
    error.value = errorMessage(caught, 'Could not read the setup status.')
  } finally {
    loading.value = false
  }
})

async function run(action: () => Promise<unknown>) {
  busy.value = true
  error.value = ''
  try {
    await action()
    await refresh()
  } catch (caught) {
    error.value = errorMessage(caught)
  } finally {
    busy.value = false
  }
}

/* ---- step state -------------------------------------------------------- */

const code = ref('')
const tenancyMode = ref<'Single' | 'Multi'>('Multi')
const tenantName = ref('')
const tenantSlug = ref('')
const storeName = ref('primary')
const storePath = ref('/var/lib/binstash/chunks')
const storeType = ref<number>(0)
const adminEmail = ref('')
const adminPassword = ref('')
const adminFirstName = ref('')
const adminLastName = ref('')

const chunkStoreTypes = ref<Array<{ name: string; value: number }>>([])
const existingStoresDismissed = ref(false)

/** Local is the only backend currently implemented server-side. */
const availableStoreTypes = computed(() =>
  chunkStoreTypes.value.length ? chunkStoreTypes.value : [{ name: 'Local', value: 0 }],
)

async function loadChunkStoreTypes() {
  try {
    chunkStoreTypes.value = await setupApi.chunkStoreTypes()
  } catch {
    // Local is the only backend currently implemented; fall back rather than block.
    chunkStoreTypes.value = [{ name: 'Local', value: 0 }]
  }
}

const claim = () => run(() => setupApi.claim(code.value.trim()).then(loadChunkStoreTypes))
const setTenancy = () => run(() => setupApi.setTenancy(tenancyMode.value))
const setDefaultTenant = () =>
  run(() => setupApi.configureDefaultTenant(tenantName.value.trim(), tenantSlug.value.trim()))
const setChunkStore = () =>
  run(() =>
    setupApi.ensureChunkStore({
      type: storeType.value,
      name: storeName.value.trim(),
      localPath: storePath.value.trim(),
    }),
  )

/**
 * Chunk stores already present when the wizard runs — the case when an existing
 * instance is upgraded. Creating a second store over the same directory would be
 * wrong, so the step offers to adopt what is there instead.
 */
const existingStores = computed(() => status.value?.data?.chunkStores ?? [])

const keepExistingChunkStores = () =>
  run(() =>
    setupApi.ensureChunkStore({
      type: storeType.value,
      name: storeName.value.trim(),
      localPath: storePath.value.trim(),
      skip: true,
    }),
  )
const setStorageClasses = () =>
  run(() => setupApi.ensureStorageClasses([...DEFAULT_STORAGE_CLASSES]))

const setStorageDefaults = () =>
  run(() => {
    const store = status.value?.data?.chunkStores?.[0]
    if (!store) throw new Error('No chunk store is available to map to.')

    return setupApi.ensureStorageDefaults(
      (status.value?.data?.storageClasses ?? []).map((storageClass, index) => ({
        chunkStoreId: store.id,
        storageClassName: storageClass.name,
        isDefault: index === 0,
        isEnabled: true,
      })),
    )
  })

const createAdmin = () =>
  run(() =>
    setupApi.createAdmin({
      isInstanceAdmin: true,
      isTenantAdmin: !isMulti.value,
      email: adminEmail.value.trim(),
      password: adminPassword.value,
      firstName: adminFirstName.value.trim() || undefined,
      lastName: adminLastName.value.trim() || undefined,
    }),
  )

async function finish() {
  await run(() => setupApi.finish())
  if (!error.value) {
    markSetupComplete()
    await router.push({ name: 'sign-in' })
  }
}
</script>

<template>
  <div class="space-y-8">
    <div v-if="loading" class="space-y-3">
      <Skeleton class="h-8 w-1/2" />
      <Skeleton class="h-40 w-full" />
    </div>

    <template v-else>
      <!-- One row always: connectors flex to fill on wide viewports and collapse to a
           minimum on narrow ones, where the strip scrolls sideways instead of wrapping. -->
      <ol class="scrollbar-thin flex w-full items-center justify-center gap-3 overflow-x-auto pb-1">
        <li v-for="(entry, index) in visibleSteps" :key="entry.key" class="flex shrink-0 items-center gap-3">
          <span class="flex shrink-0 items-center gap-2">
            <span
              class="flex size-6 shrink-0 items-center justify-center rounded-full border text-xs font-medium"
              :class="
                index < currentIndex
                  ? 'border-primary bg-primary text-primary-foreground'
                  : index === currentIndex
                    ? 'border-primary text-primary'
                    : 'border-hairline text-muted-foreground'
              "
            >
              <Check v-if="index < currentIndex" class="size-3.5" />
              <template v-else>{{ index + 1 }}</template>
            </span>
            <span
              class="text-xs whitespace-nowrap"
              :class="index === currentIndex ? 'font-medium' : 'text-muted-foreground'"
            >
              {{ entry.label }}
            </span>
          </span>

          <span
            v-if="index < visibleSteps.length - 1"
            class="bg-hairline h-px w-8 shrink-0"
            aria-hidden="true"
          />
        </li>
      </ol>

      <Alert v-if="error" variant="destructive">
        <AlertDescription>{{ error }}</AlertDescription>
      </Alert>

      <section class="bg-card hairline space-y-5 rounded-lg p-6">
        <template v-if="step === 'Claim'">
          <div class="space-y-1.5">
            <h2 class="flex items-center gap-2 text-base font-semibold">
              <KeyRound class="size-4" />
              Claim this instance
            </h2>
            <p class="text-muted-foreground text-sm">
              The server printed a one-time setup code to its log on first start. Paste it here to
              prove you control this deployment.
            </p>
          </div>

          <form class="space-y-4" @submit.prevent="claim">
            <div class="space-y-2">
              <Label for="setup-code">Setup code</Label>
              <Input
                id="setup-code"
                v-model.trim="code"
                class="font-mono"
                required
                :disabled="busy"
                autocomplete="off"
              />
            </div>
            <Button type="submit" :disabled="busy || !code.trim()">
              {{ busy ? 'Verifying…' : 'Claim instance' }}
            </Button>
          </form>
        </template>

        <template v-else-if="step === 'Tenancy'">
          <div class="space-y-1.5">
            <h2 class="text-base font-semibold">Tenancy mode</h2>
            <p class="text-muted-foreground text-sm">
              This is hard to change later, because it determines how every request resolves a
              workspace.
            </p>
          </div>

          <div class="grid gap-3 sm:grid-cols-2">
            <button
              v-for="option in [
                {
                  value: 'Single' as const,
                  title: 'Single workspace',
                  body: 'One team on this instance. No workspace switcher.',
                },
                {
                  value: 'Multi' as const,
                  title: 'Multiple workspaces',
                  body: 'Several isolated teams, resolved per request. Required for SaaS.',
                },
              ]"
              :key="option.value"
              type="button"
              class="hairline rounded-lg p-4 text-left transition-colors"
              :class="tenancyMode === option.value ? 'border-primary bg-primary/5' : ''"
              @click="tenancyMode = option.value"
            >
              <p class="text-sm font-medium">{{ option.title }}</p>
              <p class="text-muted-foreground mt-1 text-xs">{{ option.body }}</p>
            </button>
          </div>

          <Button :disabled="busy" @click="setTenancy">
            {{ busy ? 'Saving…' : 'Continue' }}
          </Button>
        </template>

        <template v-else-if="step === 'DefaultTenant'">
          <div class="space-y-1.5">
            <h2 class="text-base font-semibold">Default workspace</h2>
            <p class="text-muted-foreground text-sm">
              In single-workspace mode every request resolves to this one.
            </p>
          </div>

          <form class="space-y-4" @submit.prevent="setDefaultTenant">
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="space-y-2">
                <Label for="tenant-name">Name</Label>
                <Input id="tenant-name" v-model.trim="tenantName" required :disabled="busy" />
              </div>
              <div class="space-y-2">
                <Label for="tenant-slug">Slug</Label>
                <Input
                  id="tenant-slug"
                  v-model.trim="tenantSlug"
                  class="font-mono"
                  required
                  :disabled="busy"
                />
              </div>
            </div>
            <Button type="submit" :disabled="busy || !tenantName.trim() || !tenantSlug.trim()">
              {{ busy ? 'Saving…' : 'Continue' }}
            </Button>
          </form>
        </template>

        <template v-else-if="step === 'ChunkStore'">
          <div class="space-y-1.5">
            <h2 class="text-base font-semibold">Chunk store</h2>
            <p class="text-muted-foreground text-sm">
              Where deduplicated chunks are written. The path must be writable by the BinStash
              service account.
            </p>
          </div>

          <!-- Upgrade path: never create a second store over a directory already in use. -->
          <div v-if="existingStores.length" class="space-y-3">
            <Alert>
              <AlertDescription>
                This instance already has
                {{ existingStores.length }} chunk store{{ existingStores.length === 1 ? '' : 's' }}
                holding existing data. Keep using
                {{ existingStores.length === 1 ? 'it' : 'them' }} rather than creating another.
              </AlertDescription>
            </Alert>

            <ul class="space-y-2">
              <li
                v-for="store in existingStores"
                :key="store.id"
                class="border-hairline flex items-center justify-between gap-3 rounded-md border px-3 py-2 text-sm"
              >
                <span class="font-medium">{{ store.name }}</span>
                <span class="text-muted-foreground font-mono text-xs">{{ store.type }}</span>
              </li>
            </ul>

            <div class="flex flex-wrap gap-2">
              <Button :disabled="busy" @click="keepExistingChunkStores">
                {{ busy ? 'Saving…' : 'Keep existing and continue' }}
              </Button>
              <Button variant="outline" :disabled="busy" @click="existingStoresDismissed = true">
                Add another anyway
              </Button>
            </div>
          </div>

          <form
            v-if="!existingStores.length || existingStoresDismissed"
            class="space-y-4"
            @submit.prevent="setChunkStore"
          >
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="space-y-2">
                <Label for="store-name">Name</Label>
                <Input id="store-name" v-model.trim="storeName" required :disabled="busy" />
              </div>
              <div class="space-y-2">
                <Label for="store-type">Backend</Label>
                <Select
                  :model-value="String(storeType)"
                  :disabled="busy"
                  @update:model-value="(value) => (storeType = Number(value))"
                >
                  <SelectTrigger id="store-type" class="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem
                      v-for="type in availableStoreTypes"
                      :key="type.name"
                      :value="String(type.value)"
                    >
                      {{ type.name }}
                    </SelectItem>
                  </SelectContent>
                </Select>
              </div>
            </div>

            <div class="space-y-2">
              <Label for="store-path">Storage path</Label>
              <Input
                id="store-path"
                v-model.trim="storePath"
                class="font-mono"
                required
                :disabled="busy"
              />
            </div>

            <Button type="submit" :disabled="busy || !storeName.trim() || !storePath.trim()">
              {{ busy ? 'Creating…' : 'Continue' }}
            </Button>
          </form>
        </template>

        <template v-else-if="step === 'StorageClass'">
          <div class="space-y-1.5">
            <h2 class="text-base font-semibold">Storage classes</h2>
            <p class="text-muted-foreground text-sm">
              A storage class is the name repositories pick their backend by. Start with the
              standard one; you can add more later.
            </p>
          </div>

          <ul class="space-y-2">
            <li
              v-for="storageClass in DEFAULT_STORAGE_CLASSES"
              :key="storageClass.name"
              class="border-hairline rounded-md border px-3 py-2"
            >
              <p class="font-mono text-sm">{{ storageClass.name }}</p>
              <p class="text-muted-foreground text-xs">{{ storageClass.description }}</p>
            </li>
          </ul>

          <Button :disabled="busy" @click="setStorageClasses">
            {{ busy ? 'Saving…' : 'Continue' }}
          </Button>
        </template>

        <template v-else-if="step === 'StorageClassDefaultMappings'">
          <div class="space-y-1.5">
            <h2 class="text-base font-semibold">Storage defaults</h2>
            <p class="text-muted-foreground text-sm">
              Map each storage class onto the chunk store that backs it.
            </p>
          </div>

          <ul class="space-y-2">
            <li
              v-for="(storageClass, index) in status?.data?.storageClasses ?? []"
              :key="storageClass.name"
              class="border-hairline flex items-center justify-between gap-3 rounded-md border px-3 py-2 text-sm"
            >
              <span class="font-mono">{{ storageClass.name }}</span>
              <span class="text-muted-foreground">
                → {{ status?.data?.chunkStores?.[0]?.name ?? 'no chunk store' }}
                <span v-if="index === 0">(default)</span>
              </span>
            </li>
          </ul>

          <Button :disabled="busy" @click="setStorageDefaults">
            {{ busy ? 'Saving…' : 'Continue' }}
          </Button>
        </template>

        <template v-else-if="step === 'InstanceAdmin'">
          <div class="space-y-1.5">
            <h2 class="text-base font-semibold">Administrator account</h2>
            <p class="text-muted-foreground text-sm">
              This account administers the instance{{
                isMulti ? '' : ' and the default workspace'
              }}.
            </p>
          </div>

          <form class="space-y-4" @submit.prevent="createAdmin">
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="space-y-2">
                <Label for="admin-first">First name</Label>
                <Input id="admin-first" v-model.trim="adminFirstName" :disabled="busy" />
              </div>
              <div class="space-y-2">
                <Label for="admin-last">Last name</Label>
                <Input id="admin-last" v-model.trim="adminLastName" :disabled="busy" />
              </div>
            </div>

            <div class="space-y-2">
              <Label for="admin-email">Email</Label>
              <Input
                id="admin-email"
                v-model.trim="adminEmail"
                type="email"
                required
                :disabled="busy"
              />
            </div>

            <div class="space-y-2">
              <Label for="admin-password">Password</Label>
              <Input
                id="admin-password"
                v-model="adminPassword"
                type="password"
                autocomplete="new-password"
                required
                :disabled="busy"
              />
            </div>

            <Button type="submit" :disabled="busy || !adminEmail.trim() || !adminPassword">
              {{ busy ? 'Creating…' : 'Create administrator' }}
            </Button>
          </form>
        </template>

        <template v-else>
          <div class="space-y-1.5">
            <h2 class="flex items-center gap-2 text-base font-semibold">
              <CircleCheck class="text-success size-4" />
              Ready to finish
            </h2>
            <p class="text-muted-foreground text-sm">
              Review what will be applied, then finish setup to open the instance.
            </p>
          </div>

          <dl class="space-y-2 text-sm">
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">Tenancy</dt>
              <dd>{{ status?.data?.tenancyMode ?? '—' }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">Chunk stores</dt>
              <dd class="font-mono">{{ (status?.data?.chunkStores ?? []).length }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">Storage classes</dt>
              <dd class="font-mono">{{ (status?.data?.storageClasses ?? []).length }}</dd>
            </div>
            <div class="flex justify-between gap-4">
              <dt class="text-muted-foreground">Administrators</dt>
              <dd class="font-mono">{{ (status?.data?.instanceAdmins ?? []).length }}</dd>
            </div>
          </dl>

          <Button :disabled="busy" @click="finish">
            {{ busy ? 'Finishing…' : 'Finish setup' }}
          </Button>
        </template>
      </section>
    </template>
  </div>
</template>
