<script setup lang="ts">
import { computed, ref, watch } from 'vue'
import { useRouter } from 'vue-router'

import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Input } from '@/components/ui/input'
import { Label } from '@/components/ui/label'
import { useMutation } from '@/composables/useGraphql'
import { CreateTenantDocument } from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'
import { useTenantStore } from '@/stores/tenant'

/**
 * Replaces the previous four-route Onboarding01–04 wizard. Naming a workspace and
 * picking a slug is one decision, and four screens made it feel like four.
 */
const router = useRouter()
const tenants = useTenantStore()
const { mutate, loading } = useMutation(CreateTenantDocument)

const name = ref('')
const slug = ref('')
const slugEdited = ref(false)
const error = ref('')

function slugify(value: string) {
  return value
    .toLowerCase()
    .normalize('NFKD')
    .replace(/[^\w\s-]/g, '')
    .trim()
    .replace(/[\s_]+/g, '-')
    .replace(/-+/g, '-')
    .slice(0, 48)
}

// Derive the slug until the user takes it over, then leave it alone.
watch(name, (value) => {
  if (!slugEdited.value) slug.value = slugify(value)
})

const canSubmit = computed(() => name.value.trim().length > 0 && slug.value.length > 0)

async function submit() {
  error.value = ''

  try {
    const created = await mutate({ input: { name: name.value.trim(), slug: slug.value } })
    if (!created?.createTenant) throw new Error('The workspace was not created.')

    await tenants.load()
    await tenants.switchTenant(created.createTenant.id)
    await router.push({ name: 'tenant-home', params: { tenantId: created.createTenant.id } })
  } catch (caught) {
    error.value = errorMessage(caught, 'Could not create the workspace.')
  }
}
</script>

<template>
  <div class="space-y-6">
    <div class="space-y-1.5">
      <h1 class="text-xl font-semibold tracking-tight">Create a workspace</h1>
      <p class="text-muted-foreground text-sm">
        A workspace holds your repositories, releases and team.
      </p>
    </div>

    <Alert v-if="error" variant="destructive">
      <AlertDescription>{{ error }}</AlertDescription>
    </Alert>

    <form class="space-y-4" @submit.prevent="submit">
      <div class="space-y-2">
        <Label for="name">Workspace name</Label>
        <Input id="name" v-model.trim="name" required :disabled="loading" placeholder="Acme Corp" />
      </div>

      <div class="space-y-2">
        <Label for="slug">Slug</Label>
        <Input
          id="slug"
          v-model.trim="slug"
          class="font-mono"
          required
          :disabled="loading"
          @input="slugEdited = true"
        />
        <p class="text-muted-foreground text-xs">
          Lowercase letters, numbers and hyphens. Used in URLs and by the CLI.
        </p>
      </div>

      <Button type="submit" class="w-full" :disabled="loading || !canSubmit">
        {{ loading ? 'Creating…' : 'Create workspace' }}
      </Button>
    </form>

    <p v-if="tenants.hasTenants" class="text-center">
      <RouterLink
        :to="{ name: 'select-tenant' }"
        class="text-muted-foreground hover:text-foreground text-sm"
      >
        Back to your workspaces
      </RouterLink>
    </p>
  </div>
</template>
