<script setup lang="ts">
import { Monitor, Moon, Sun } from '@lucide/vue'

import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { useThemeStore } from '@/stores/theme'
import type { ThemePreference } from '@/stores/theme'

const theme = useThemeStore()

const options: Array<{ value: ThemePreference; label: string; icon: typeof Sun }> = [
  { value: 'dark', label: 'Dark', icon: Moon },
  { value: 'light', label: 'Light', icon: Sun },
  { value: 'system', label: 'System', icon: Monitor },
]
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" size="icon" aria-label="Change theme">
        <Moon v-if="theme.isDark" class="size-4" />
        <Sun v-else class="size-4" />
      </Button>
    </DropdownMenuTrigger>
    <DropdownMenuContent align="end" class="w-36">
      <DropdownMenuItem
        v-for="option in options"
        :key="option.value"
        class="gap-2"
        :data-active="theme.preference === option.value ? '' : undefined"
        @select="theme.setPreference(option.value)"
      >
        <component :is="option.icon" class="size-4" />
        <span>{{ option.label }}</span>
        <span v-if="theme.preference === option.value" class="text-primary ml-auto text-xs">•</span>
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
