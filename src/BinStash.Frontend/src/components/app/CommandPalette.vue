<script setup lang="ts">
import {
  Boxes,
  Database,
  FolderGit2,
  Gauge,
  KeyRound,
  ScrollText,
  Settings,
  Shield,
  Users,
} from '@lucide/vue'
import { useMagicKeys, useDebounce, whenever } from '@vueuse/core'
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import {
  CommandDialog,
  CommandEmpty,
  CommandGroup,
  CommandInput,
  CommandItem,
  CommandList,
} from '@/components/ui/command'
import { SearchRepositoriesDocument } from '@/graphql/generated'
import { useCommandPalette } from '@/composables/useCommandPalette'
import { useQuery } from '@/composables/useGraphql'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'

/**
 * Replaces the previous client-side Fuse.js index, which searched a hand-maintained
 * static list and therefore quietly went stale as data grew. Navigation is still local
 * (it is a fixed, known set), but entity lookup goes to the server.
 */
const { open, toggle } = useCommandPalette()
const term = ref('')
const debouncedTerm = useDebounce(term, 180)

const router = useRouter()
const auth = useAuthStore()
const tenants = useTenantStore()

const { Meta_K, Ctrl_K } = useMagicKeys({
  passive: false,
  onEventFired(event) {
    if (event.key?.toLowerCase() === 'k' && (event.metaKey || event.ctrlKey)) event.preventDefault()
  },
})

whenever(Meta_K, toggle)
whenever(Ctrl_K, toggle)

// A single character matches nearly everything, so hold off until the term is worth a round trip.
const searchable = computed(() => debouncedTerm.value.trim().length >= 2)

const { result } = useQuery(
  SearchRepositoriesDocument,
  () => ({ term: debouncedTerm.value.trim(), first: 6 }),
  { enabled: searchable },
)

watch(open, (isOpen) => {
  if (!isOpen) term.value = ''
})

const repositories = computed(() => result.value?.repositories?.nodes ?? [])

interface NavCommand {
  label: string
  icon: typeof Gauge
  to: { name: string }
  visible?: boolean
}

const navCommands = computed<NavCommand[]>(() =>
  [
    { label: 'Overview', icon: Gauge, to: { name: 'tenant-home' } },
    { label: 'Repositories', icon: FolderGit2, to: { name: 'repositories' } },
    { label: 'Members', icon: Users, to: { name: 'members' } },
    { label: 'Service accounts', icon: KeyRound, to: { name: 'service-accounts' } },
    { label: 'Usage', icon: Boxes, to: { name: 'usage' } },
    { label: 'Audit log', icon: ScrollText, to: { name: 'audit' } },
    { label: 'Workspace settings', icon: Settings, to: { name: 'tenant-settings' } },
    {
      label: 'Instance administration',
      icon: Shield,
      to: { name: 'instance-home' },
      visible: auth.isInstanceAdmin,
    },
    {
      label: 'Chunk stores',
      icon: Database,
      to: { name: 'chunk-stores' },
      visible: auth.isInstanceAdmin,
    },
  ].filter((command) => command.visible !== false),
)

function go(to: { name: string }, params: Record<string, string> = {}) {
  open.value = false
  void router.push({
    name: to.name,
    params: { tenantId: tenants.activeTenantId ?? undefined, ...params },
  })
}
</script>

<template>
  <CommandDialog v-model:open="open">
    <CommandInput v-model="term" placeholder="Search repositories, jump to a page…" />
    <CommandList>
      <CommandEmpty>No results.</CommandEmpty>

      <CommandGroup v-if="repositories.length" heading="Repositories">
        <CommandItem
          v-for="repo in repositories"
          :key="repo.id"
          :value="`repo-${repo.id}`"
          class="gap-2"
          @select="go({ name: 'repository' }, { repoId: repo.id })"
        >
          <FolderGit2 class="text-muted-foreground size-4" />
          <span class="flex-1 truncate">{{ repo.name }}</span>
          <span class="text-muted-foreground font-mono text-xs">{{ repo.storageClass }}</span>
        </CommandItem>
      </CommandGroup>

      <CommandGroup heading="Go to">
        <CommandItem
          v-for="command in navCommands"
          :key="command.label"
          :value="command.label"
          class="gap-2"
          @select="go(command.to)"
        >
          <component :is="command.icon" class="text-muted-foreground size-4" />
          <span>{{ command.label }}</span>
        </CommandItem>
      </CommandGroup>
    </CommandList>
  </CommandDialog>
</template>
