<template>
  <BaseCard :padded="false" class="col-span-full sm:col-span-6 xl:col-span-3 transition hover:border-accent/30">
    <div class="flex h-full flex-col">
      <!-- Card top -->
      <div class="grow p-5">
        <header>
          <div class="mb-3 flex justify-center">
            <router-link
              :to="item.link"
              class="flex h-16 w-16 items-center justify-center rounded-2xl bg-accent-soft text-accent"
            >
              <IconDatabase class="h-8 w-8" />
            </router-link>
          </div>
          <div class="text-center">
            <router-link
              :to="item.link"
              class="inline-flex text-ink-strong transition hover:text-accent"
            >
              <h2 class="justify-center text-xl font-semibold leading-snug">{{ item.name }}</h2>
            </router-link>
          </div>
          <div v-if="item.location" class="mt-0.5 flex items-center justify-center gap-1 text-sm text-ink-subtle">
            <span>{{ item.location }}</span>
          </div>
        </header>
      </div>
      <!-- Card footer -->
      <div class="border-t border-hairline">
        <router-link
          :to="item.link"
          class="flex items-center justify-center gap-2 px-3 py-4 text-center text-sm font-medium text-accent transition hover:brightness-110"
        >
          <IconPencil class="h-4 w-4 shrink-0" />
          <span>View Details</span>
        </router-link>
      </div>
    </div>
  </BaseCard>
</template>

<script lang="ts">
import { BaseCard } from '@/shared/components/ui'
import { IconDatabase, IconPencil } from '@tabler/icons-vue'

export default {
  name: 'ChunkStoreCard',
  props: ['item'],
  components: {
    BaseCard,
    IconDatabase,
    IconPencil,
  },
}
</script>
