<template>
  <router-link :to="to" custom v-slot="{ href, navigate, isActive, isExactActive }">
    <li>
      <a
        :href="href"
        class="group flex items-center gap-3 rounded-control px-3 py-2 text-sm font-medium transition lg:justify-center lg:sidebar-expanded:justify-start"
        :class="(exact ? isExactActive : isActive)
          ? 'bg-accent-soft text-accent'
          : 'text-ink-muted hover:bg-raised hover:text-ink-strong'"
        @click="navigate"
      >
        <component
          :is="icon"
          class="h-5 w-5 shrink-0 transition"
          :class="(exact ? isExactActive : isActive) ? 'text-accent' : 'text-ink-subtle group-hover:text-ink-muted'"
        />
        <span class="whitespace-nowrap lg:hidden lg:sidebar-expanded:inline">{{ label }}</span>
      </a>
    </li>
  </router-link>
</template>

<script setup lang="ts">
import type { Component } from 'vue'

defineProps<{
  to: string | object
  icon: Component
  label: string
  exact?: boolean
}>()
</script>
