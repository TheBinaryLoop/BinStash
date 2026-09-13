<script setup lang="ts">
import { LogOut, Shield, UserRound } from '@lucide/vue'
import { useRouter } from 'vue-router'

import { Avatar, AvatarFallback } from '@/components/ui/avatar'
import { Button } from '@/components/ui/button'
import {
  DropdownMenu,
  DropdownMenuContent,
  DropdownMenuItem,
  DropdownMenuLabel,
  DropdownMenuSeparator,
  DropdownMenuTrigger,
} from '@/components/ui/dropdown-menu'
import { initialsOf } from '@/lib/format'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'

const auth = useAuthStore()
const tenants = useTenantStore()
const router = useRouter()

async function signOut() {
  await auth.signOut()
  tenants.reset()
  await router.push({ name: 'sign-in' })
}
</script>

<template>
  <DropdownMenu>
    <DropdownMenuTrigger as-child>
      <Button variant="ghost" size="icon" class="rounded-full" aria-label="Account menu">
        <Avatar class="size-7">
          <AvatarFallback class="bg-secondary text-xs font-medium">
            {{ initialsOf(auth.user?.firstName, auth.user?.lastName) }}
          </AvatarFallback>
        </Avatar>
      </Button>
    </DropdownMenuTrigger>

    <DropdownMenuContent align="end" class="w-60">
      <DropdownMenuLabel class="space-y-0.5">
        <p class="truncate text-sm font-medium">{{ auth.displayName }}</p>
        <p class="text-muted-foreground truncate text-xs font-normal">{{ auth.user?.email }}</p>
      </DropdownMenuLabel>
      <DropdownMenuSeparator />

      <DropdownMenuItem class="gap-2" @select="router.push({ name: 'select-tenant' })">
        <UserRound class="size-4" />
        <span>Switch workspace</span>
      </DropdownMenuItem>
      <DropdownMenuItem
        v-if="auth.isInstanceAdmin"
        class="gap-2"
        @select="router.push({ name: 'instance-home' })"
      >
        <Shield class="size-4" />
        <span>Instance administration</span>
      </DropdownMenuItem>

      <DropdownMenuSeparator />
      <DropdownMenuItem class="text-destructive gap-2" @select="signOut">
        <LogOut class="size-4" />
        <span>Sign out</span>
      </DropdownMenuItem>
    </DropdownMenuContent>
  </DropdownMenu>
</template>
