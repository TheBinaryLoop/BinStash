<script setup lang="ts">
import { Download, Package } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { useRoute } from 'vue-router'

import AsyncSection from '@/components/app/AsyncSection.vue'
import CopyButton from '@/components/app/CopyButton.vue'
import PageBreadcrumbs from '@/components/app/PageBreadcrumbs.vue'
import PageHeader from '@/components/app/PageHeader.vue'
import { Badge } from '@/components/ui/badge'
import { Button } from '@/components/ui/button'
import { useQuery } from '@/composables/useGraphql'
import { ReleaseDocument } from '@/graphql/generated'
import { formatBytes, formatDate, formatNumber } from '@/lib/format'
import { useTenantStore } from '@/stores/tenant'

const route = useRoute()
const tenants = useTenantStore()

const releaseId = computed(() => route.params.releaseId as string)
const repoId = computed(() => route.params.repoId as string)

const { result, loading, error, refetch } = useQuery(ReleaseDocument, () => ({
  id: releaseId.value,
}))

const release = computed(() => result.value?.release)
const metrics = computed(() => release.value?.metrics)

const variants = computed(() => release.value?.variants ?? [])

/**
 * A release that declares no target still has exactly one variant, keyed `default`. Showing a
 * one-item switcher for it would invent a choice the publisher never made, so the target UI only
 * appears once there is something to choose between.
 */
const hasTargets = computed(() => variants.value.length > 1)

const selectedTarget = ref<string | null>(null)

// Reset when navigating between releases, and default to the first target published.
watch(variants, (list) => {
  selectedTarget.value = list.length > 1 ? (list[0]?.targetKey ?? null) : null
}, { immediate: true })

const selectedVariant = computed(
  () => variants.value.find((v) => v.targetKey === selectedTarget.value) ?? variants.value[0],
)

/**
 * The package download is binary and stays on REST — it is not a GraphQL-shaped
 * operation. The route is tenant-prefixed on the server.
 */
const downloadUrl = computed(() => {
  const base = `/api/tenants/${tenants.activeTenantId}/repositories/${repoId.value}/releases/${releaseId.value}/download`
  // The server refuses an unqualified download once a release has several targets, rather than
  // guessing — so the target has to travel with the request.
  return hasTargets.value && selectedTarget.value
    ? `${base}?target=${encodeURIComponent(selectedTarget.value)}`
    : base
})

const cliCommand = computed(() => {
  const repo = release.value?.repository
  if (!repo) return ''
  const target = hasTargets.value && selectedTarget.value ? ` --target ${selectedTarget.value}` : ''
  return `binstash release get --repo ${repo.name} --version ${release.value!.version}${target}`
})

const details = computed(() => {
  const stats = metrics.value
  // With several targets the release totals are sums across all of them, which is not what
  // someone looking at one target wants to read. Show the selected target's own figures.
  const variant = hasTargets.value ? selectedVariant.value : undefined
  return [
    { label: 'Size', value: formatBytes(variant?.totalLogicalBytes ?? stats?.totalLogicalBytes ?? null) },
    { label: 'Files', value: formatNumber(variant?.filesInVariant ?? stats?.filesInRelease ?? null) },
    { label: 'Components', value: formatNumber(stats?.componentsInRelease ?? null) },
    { label: 'Chunks', value: formatNumber(stats?.chunksInRelease ?? null) },
    { label: 'Metadata', value: formatBytes(stats?.metaBytesFull ?? null) },
  ]
})

/** Publisher-supplied metadata, attached at publish time with the CLI's `-p key=value`. */
const customProperties = computed(() => release.value?.customProperties ?? [])

</script>

<template>
  <div class="space-y-6">
    <PageBreadcrumbs
      :items="[
        { label: 'Repositories', to: { name: 'repositories', params: { tenantId: tenants.activeTenantId } } },
        {
          label: release?.repository?.name ?? '…',
          to: { name: 'repository', params: { tenantId: tenants.activeTenantId, repoId } },
        },
        { label: release?.version ?? '…' },
      ]"
    />

    <AsyncSection
      :loading="loading"
      :error="error"
      :has-data="!!release"
      :skeleton-rows="4"
      @retry="refetch()"
    >
      <div v-if="release" class="space-y-6">
        <PageHeader :title="release.version">
          <template #badge>
            <Badge variant="secondary" class="font-mono">
              {{ release.repository?.storageClass }}
            </Badge>
          </template>
          <template #meta>
            <div class="text-muted-foreground flex flex-wrap items-center gap-x-4 gap-y-1 text-xs">
              <span>Published {{ formatDate(release.createdAt) }}</span>
              <span class="flex items-center gap-1.5">
                <span class="font-mono">{{ release.id.slice(0, 8) }}</span>
                <CopyButton :value="release.id" label="ID" />
              </span>
            </div>
          </template>
          <template #actions>
            <div class="flex flex-wrap items-center gap-2">
              <div
                v-if="hasTargets"
                class="bg-muted/50 hairline flex items-center gap-0.5 rounded-lg p-0.5"
                role="group"
                aria-label="Build target"
              >
                <button
                  v-for="variant in variants"
                  :key="variant.id"
                  type="button"
                  class="rounded-md px-2.5 py-1 font-mono text-xs transition-colors"
                  :class="
                    variant.targetKey === selectedTarget
                      ? 'bg-background text-foreground shadow-sm'
                      : 'text-muted-foreground hover:text-foreground'
                  "
                  :aria-pressed="variant.targetKey === selectedTarget"
                  @click="selectedTarget = variant.targetKey"
                >
                  {{ variant.targetKey }}
                </button>
              </div>
              <Button as="a" :href="downloadUrl" download class="gap-2">
                <Download class="size-4" />
                Download
              </Button>
            </div>
          </template>
        </PageHeader>

        <div class="grid gap-4 lg:grid-cols-3">
          <!-- Release notes take the wide column: they are free-form and can run long. -->
          <section class="bg-card hairline h-fit space-y-3 rounded-lg p-5 lg:col-span-2">
            <h2 class="text-sm font-medium">Release notes</h2>
            <p
              v-if="release.notes"
              class="text-muted-foreground text-sm leading-relaxed whitespace-pre-wrap"
            >
              {{ release.notes }}
            </p>
            <p v-else class="text-muted-foreground text-sm">
              No notes were provided for this release.
            </p>
          </section>

          <div class="space-y-4">
            <section class="bg-card hairline space-y-3 rounded-lg p-5">
              <h2 class="text-sm font-medium">Details</h2>
              <dl class="space-y-2">
                <div
                  v-for="item in details"
                  :key="item.label"
                  class="flex justify-between gap-4 text-sm"
                >
                  <dt class="text-muted-foreground">{{ item.label }}</dt>
                  <dd class="font-mono tabular-nums">{{ item.value }}</dd>
                </div>
              </dl>
            </section>

            <section class="bg-card hairline space-y-3 rounded-lg p-5">
              <h2 class="text-sm font-medium">Custom properties</h2>
              <dl v-if="customProperties.length" class="space-y-2">
                <div
                  v-for="property in customProperties"
                  :key="property.key"
                  class="flex justify-between gap-4 text-xs"
                >
                  <dt class="text-muted-foreground truncate">{{ property.key }}</dt>
                  <dd class="truncate font-mono" :title="property.value">{{ property.value }}</dd>
                </div>
              </dl>
              <p v-else class="text-muted-foreground text-sm">None.</p>
            </section>

            <section class="bg-card hairline space-y-2 rounded-lg p-5">
              <div class="flex items-center justify-between gap-2">
                <h2 class="text-sm font-medium">Fetch from the CLI</h2>
                <CopyButton :value="cliCommand" />
              </div>
              <code
                class="bg-muted text-muted-foreground block overflow-x-auto rounded p-2 font-mono text-xs"
              >
                {{ cliCommand }}
              </code>
            </section>
          </div>
        </div>
      </div>

      <div v-else class="text-muted-foreground flex flex-col items-center gap-3 py-16">
        <Package class="size-6" />
        <p class="text-sm">Release not found.</p>
      </div>
    </AsyncSection>
  </div>
</template>
