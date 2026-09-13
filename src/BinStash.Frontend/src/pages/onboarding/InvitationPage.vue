<script setup lang="ts">
import { CircleCheck, MailQuestion } from '@lucide/vue'
import { computed, ref } from 'vue'
import { useRoute, useRouter } from 'vue-router'

import { Alert, AlertDescription } from '@/components/ui/alert'
import { Button } from '@/components/ui/button'
import { Skeleton } from '@/components/ui/skeleton'
import { useMutation, useQuery } from '@/composables/useGraphql'
import { AcceptTenantInvitationDocument, TenantInvitationPreviewDocument } from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'
import { formatDate } from '@/lib/format'
import { useAuthStore } from '@/stores/auth'
import { useTenantStore } from '@/stores/tenant'

const route = useRoute()
const router = useRouter()
const auth = useAuthStore()
const tenants = useTenantStore()

const tenantId = route.params.tenantId as string
const code = route.params.code as string

// The preview is intentionally anonymous so an invitee can see what they were invited to
// before creating an account.
const { result, loading, error } = useQuery(TenantInvitationPreviewDocument, { tenantId, code })
const invitation = computed(() => result.value?.tenantInvitationPreview ?? null)

const { mutate, loading: accepting } = useMutation(AcceptTenantInvitationDocument)
const acceptError = ref('')
const accepted = ref(false)

const expired = computed(() =>
  invitation.value ? new Date(invitation.value.expiresAt).getTime() < Date.now() : false,
)

async function accept() {
  acceptError.value = ''
  try {
    await mutate({ tenantId, code })
    accepted.value = true
    await tenants.load()
    await tenants.switchTenant(tenantId)
    await router.push({ name: 'tenant-home', params: { tenantId } })
  } catch (caught) {
    acceptError.value = errorMessage(caught, 'Could not accept this invitation.')
  }
}

function signInThenAccept() {
  void router.push({ name: 'sign-in', query: { redirect: route.fullPath } })
}

function signUpThenAccept() {
  void router.push({ name: 'sign-up', query: { redirect: route.fullPath, invitation: code } })
}
</script>

<template>
  <div class="space-y-6">
    <div v-if="loading" class="space-y-3">
      <Skeleton class="h-8 w-2/3" />
      <Skeleton class="h-20 w-full" />
    </div>

    <div v-else-if="error || !invitation" class="space-y-4 text-center">
      <div
        class="bg-muted text-muted-foreground mx-auto flex size-11 items-center justify-center rounded-lg"
      >
        <MailQuestion class="size-5" />
      </div>
      <div class="space-y-1.5">
        <h1 class="text-xl font-semibold tracking-tight">Invitation not found</h1>
        <p class="text-muted-foreground text-sm">
          This invitation may have been withdrawn or already used.
        </p>
      </div>
      <Button variant="outline" class="w-full" @click="router.push({ name: 'sign-in' })">
        Go to sign in
      </Button>
    </div>

    <template v-else>
      <div class="space-y-1.5">
        <h1 class="text-xl font-semibold tracking-tight">
          Join {{ invitation.tenantName }}
        </h1>
        <p class="text-muted-foreground text-sm">
          You have been invited as <span class="font-medium">{{ invitation.role }}</span
          ><template v-if="invitation.invitedEmail">
            at <span class="font-medium">{{ invitation.invitedEmail }}</span></template
          >.
        </p>
      </div>

      <div class="bg-card hairline space-y-2 rounded-lg p-4 text-sm">
        <div class="flex justify-between gap-4">
          <span class="text-muted-foreground">Workspace</span>
          <span class="font-medium">{{ invitation.tenantName }}</span>
        </div>
        <div v-if="invitation.tenantSlug" class="flex justify-between gap-4">
          <span class="text-muted-foreground">Slug</span>
          <span class="font-mono text-xs">{{ invitation.tenantSlug }}</span>
        </div>
        <div class="flex justify-between gap-4">
          <span class="text-muted-foreground">Expires</span>
          <span :class="expired ? 'text-destructive' : ''">{{ formatDate(invitation.expiresAt) }}</span>
        </div>
      </div>

      <Alert v-if="expired" variant="destructive">
        <AlertDescription>This invitation has expired. Ask for a new one.</AlertDescription>
      </Alert>
      <Alert v-else-if="acceptError" variant="destructive">
        <AlertDescription>{{ acceptError }}</AlertDescription>
      </Alert>

      <div v-if="accepted" class="text-success flex items-center gap-2 text-sm">
        <CircleCheck class="size-4" />
        Invitation accepted.
      </div>

      <!-- Accepting binds the invitation to an account, so it needs one first. -->
      <template v-else-if="auth.isAuthenticated">
        <Button class="w-full" :disabled="accepting || expired" @click="accept">
          {{ accepting ? 'Joining…' : `Join ${invitation.tenantName}` }}
        </Button>
        <p class="text-muted-foreground text-center text-xs">
          Joining as {{ auth.user?.email }}
        </p>
      </template>

      <div v-else class="space-y-2">
        <Button class="w-full" :disabled="expired" @click="signUpThenAccept">
          Create an account to join
        </Button>
        <Button variant="outline" class="w-full" :disabled="expired" @click="signInThenAccept">
          I already have an account
        </Button>
      </div>
    </template>
  </div>
</template>
