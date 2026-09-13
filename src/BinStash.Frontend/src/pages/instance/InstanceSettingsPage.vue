<script setup lang="ts">
import { Globe, Mail, Send, Users } from '@lucide/vue'
import { computed, ref, watch } from 'vue'
import { toast } from 'vue-sonner'

import PageHeader from '@/components/app/PageHeader.vue'
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
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useMutation, useQuery } from '@/composables/useGraphql'
import {
  DomainConfigDocument,
  EmailConfigDocument,
  SendTestEmailDocument,
  SetDomainConfigDocument,
  SetEmailConfigDocument,
  SetTenancyConfigDocument,
  TenancyConfigDocument,
} from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'

/**
 * The server masks stored secrets as "****" and treats a submitted "****" as
 * "keep the existing value". Round-tripping the mask is therefore correct, and the
 * fields are annotated so nobody assumes the real secret is on screen.
 */
const SECRET_MASK = '****'

const tab = ref('email')

/* ---- email ------------------------------------------------------------- */

const email = useQuery(EmailConfigDocument, {})
const { mutate: saveEmail, loading: savingEmail } = useMutation(SetEmailConfigDocument)
const { mutate: sendTest, loading: sendingTest } = useMutation(SendTestEmailDocument)

const provider = ref('Smtp')
const fromEmail = ref('')
const supportEmail = ref('')
const brevoApiKey = ref('')
const smtpHost = ref('')
const smtpPort = ref<number | undefined>(undefined)
const smtpUsername = ref('')
const smtpPassword = ref('')
const smtpSecurity = ref('StartTls')

watch(
  () => email.result.value?.emailConfig,
  (config) => {
    if (!config) return
    provider.value = config.provider ?? 'Smtp'
    fromEmail.value = config.shared?.fromEmail ?? ''
    supportEmail.value = config.shared?.supportEmail ?? ''
    brevoApiKey.value = config.brevo?.apiKey ?? ''
    smtpHost.value = config.smtp?.host ?? ''
    smtpPort.value = config.smtp?.port ?? undefined
    smtpUsername.value = config.smtp?.username ?? ''
    smtpPassword.value = config.smtp?.password ?? ''
    smtpSecurity.value = config.smtp?.security ?? 'StartTls'
  },
  { immediate: true },
)

async function submitEmail() {
  try {
    await saveEmail({
      input: {
        provider: provider.value,
        shared: { fromEmail: fromEmail.value, supportEmail: supportEmail.value },
        brevo: { apiKey: brevoApiKey.value || undefined },
        smtp: {
          host: smtpHost.value,
          port: smtpPort.value,
          username: smtpUsername.value,
          password: smtpPassword.value || undefined,
          security: smtpSecurity.value,
        },
      },
    })
    await email.refetch()
    toast.success('Email settings saved.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not save email settings.'))
  }
}

const testRecipient = ref('')

async function submitTest() {
  try {
    const response = await sendTest({ recipientEmail: testRecipient.value.trim() })
    const outcome = response?.sendTestEmail
    if (outcome?.success) toast.success(`Test email sent to ${testRecipient.value.trim()}.`)
    else toast.error(outcome?.providerError ?? 'The provider rejected the message.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not send the test email.'))
  }
}

/* ---- tenancy ----------------------------------------------------------- */

const tenancy = useQuery(TenancyConfigDocument, {})
const { mutate: saveTenancy, loading: savingTenancy } = useMutation(SetTenancyConfigDocument)

const tenancyMode = ref('Multi')
const defaultTenantId = ref('')

watch(
  () => tenancy.result.value?.tenancyConfig,
  (config) => {
    if (!config) return
    tenancyMode.value = config.mode ?? 'Multi'
    defaultTenantId.value = config.defaultTenantId ?? ''
  },
  { immediate: true },
)

async function submitTenancy() {
  try {
    await saveTenancy({
      input: {
        mode: tenancyMode.value,
        defaultTenantId: tenancyMode.value === 'Single' ? defaultTenantId.value : undefined,
      },
    })
    await tenancy.refetch()
    toast.success('Tenancy settings saved.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not save tenancy settings.'))
  }
}

/* ---- domain ------------------------------------------------------------ */

const domain = useQuery(DomainConfigDocument, {})
const { mutate: saveDomain, loading: savingDomain } = useMutation(SetDomainConfigDocument)

const baseUrl = ref('')

watch(
  () => domain.result.value?.domainConfig,
  (config) => {
    if (config) baseUrl.value = config.baseUrl ?? ''
  },
  { immediate: true },
)

async function submitDomain() {
  try {
    await saveDomain({ input: { baseUrl: baseUrl.value.trim() } })
    await domain.refetch()
    toast.success('Domain settings saved.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not save domain settings.'))
  }
}

const secretHint = computed(
  () => `Shown as ${SECRET_MASK} when stored. Leave unchanged to keep the current value.`,
)
</script>

<template>
  <div class="space-y-6">
    <PageHeader
      title="Instance settings"
      description="Configuration that applies to every tenant on this deployment."
    />

    <Tabs v-model="tab" class="space-y-4">
      <TabsList>
        <TabsTrigger value="email" class="gap-1.5">
          <Mail class="size-3.5" />
          Email
        </TabsTrigger>
        <TabsTrigger value="tenancy" class="gap-1.5">
          <Users class="size-3.5" />
          Tenancy
        </TabsTrigger>
        <TabsTrigger value="domain" class="gap-1.5">
          <Globe class="size-3.5" />
          Domain
        </TabsTrigger>
      </TabsList>

      <TabsContent value="email" class="space-y-4">
        <section class="bg-card hairline space-y-4 rounded-lg p-5">
          <div>
            <h2 class="text-sm font-medium">Delivery</h2>
            <p class="text-muted-foreground text-xs">
              Used for confirmations, password resets and workspace invitations.
            </p>
          </div>

          <form class="space-y-4" @submit.prevent="submitEmail">
            <div class="grid gap-4 sm:grid-cols-2">
              <div class="space-y-2">
                <Label for="provider">Provider</Label>
                <Select v-model="provider" :disabled="savingEmail">
                  <SelectTrigger id="provider" class="w-full"><SelectValue /></SelectTrigger>
                  <SelectContent>
                    <SelectItem value="Smtp">SMTP</SelectItem>
                    <SelectItem value="Brevo">Brevo</SelectItem>
                  </SelectContent>
                </Select>
              </div>

              <div class="space-y-2">
                <Label for="from-email">From address</Label>
                <Input
                  id="from-email"
                  v-model.trim="fromEmail"
                  type="email"
                  :disabled="savingEmail"
                />
              </div>

              <div class="space-y-2">
                <Label for="support-email">Support address</Label>
                <Input
                  id="support-email"
                  v-model.trim="supportEmail"
                  type="email"
                  :disabled="savingEmail"
                />
              </div>
            </div>

            <template v-if="provider === 'Smtp'">
              <div class="grid gap-4 sm:grid-cols-2">
                <div class="space-y-2">
                  <Label for="smtp-host">Host</Label>
                  <Input id="smtp-host" v-model.trim="smtpHost" :disabled="savingEmail" />
                </div>
                <div class="space-y-2">
                  <Label for="smtp-port">Port</Label>
                  <Input
                    id="smtp-port"
                    v-model.number="smtpPort"
                    type="number"
                    class="font-mono"
                    :disabled="savingEmail"
                  />
                </div>
                <div class="space-y-2">
                  <Label for="smtp-username">Username</Label>
                  <Input id="smtp-username" v-model.trim="smtpUsername" :disabled="savingEmail" />
                </div>
                <div class="space-y-2">
                  <Label for="smtp-password">Password</Label>
                  <Input
                    id="smtp-password"
                    v-model="smtpPassword"
                    type="password"
                    :disabled="savingEmail"
                  />
                  <p class="text-muted-foreground text-xs">{{ secretHint }}</p>
                </div>
                <div class="space-y-2">
                  <Label for="smtp-security">Security</Label>
                  <Select v-model="smtpSecurity" :disabled="savingEmail">
                    <SelectTrigger id="smtp-security" class="w-full"><SelectValue /></SelectTrigger>
                    <SelectContent>
                      <SelectItem value="None">None</SelectItem>
                      <SelectItem value="StartTls">STARTTLS</SelectItem>
                      <SelectItem value="SslOnConnect">SSL on connect</SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>
            </template>

            <div v-else class="space-y-2">
              <Label for="brevo-key">Brevo API key</Label>
              <Input id="brevo-key" v-model="brevoApiKey" type="password" :disabled="savingEmail" />
              <p class="text-muted-foreground text-xs">{{ secretHint }}</p>
            </div>

            <Button type="submit" :disabled="savingEmail">
              {{ savingEmail ? 'Saving…' : 'Save email settings' }}
            </Button>
          </form>
        </section>

        <section class="bg-card hairline space-y-3 rounded-lg p-5">
          <h2 class="text-sm font-medium">Send a test email</h2>
          <form class="flex flex-col gap-2 sm:flex-row" @submit.prevent="submitTest">
            <Input
              v-model.trim="testRecipient"
              type="email"
              placeholder="you@example.com"
              class="sm:max-w-sm"
              :disabled="sendingTest"
            />
            <Button
              type="submit"
              variant="outline"
              class="gap-2"
              :disabled="sendingTest || !testRecipient.trim()"
            >
              <Send class="size-4" />
              {{ sendingTest ? 'Sending…' : 'Send test' }}
            </Button>
          </form>
        </section>
      </TabsContent>

      <TabsContent value="tenancy">
        <section class="bg-card hairline space-y-4 rounded-lg p-5">
          <div>
            <h2 class="text-sm font-medium">Tenancy mode</h2>
            <p class="text-muted-foreground text-xs">
              Single-tenant resolves every request to one fixed workspace. Multi-tenant resolves it
              per request from the host, path or header.
            </p>
          </div>

          <Alert v-if="tenancyMode === 'Single'">
            <AlertDescription>
              In single-tenant mode the workspace switcher is bypassed and all requests use the
              default workspace below.
            </AlertDescription>
          </Alert>

          <form class="space-y-4" @submit.prevent="submitTenancy">
            <div class="space-y-2">
              <Label for="tenancy-mode">Mode</Label>
              <Select v-model="tenancyMode" :disabled="savingTenancy">
                <SelectTrigger id="tenancy-mode" class="sm:w-64"><SelectValue /></SelectTrigger>
                <SelectContent>
                  <SelectItem value="Multi">Multi-tenant</SelectItem>
                  <SelectItem value="Single">Single-tenant</SelectItem>
                </SelectContent>
              </Select>
            </div>

            <div v-if="tenancyMode === 'Single'" class="space-y-2">
              <Label for="default-tenant">Default workspace ID</Label>
              <Input
                id="default-tenant"
                v-model.trim="defaultTenantId"
                class="font-mono sm:max-w-md"
                :disabled="savingTenancy"
              />
            </div>

            <Button type="submit" :disabled="savingTenancy">
              {{ savingTenancy ? 'Saving…' : 'Save tenancy settings' }}
            </Button>
          </form>
        </section>
      </TabsContent>

      <TabsContent value="domain">
        <section class="bg-card hairline space-y-4 rounded-lg p-5">
          <div>
            <h2 class="text-sm font-medium">Base URL</h2>
            <p class="text-muted-foreground text-xs">
              Used to build the links in confirmation and invitation emails, so it must be the URL
              users actually reach this instance on.
            </p>
          </div>

          <form class="space-y-4" @submit.prevent="submitDomain">
            <div class="space-y-2">
              <Label for="base-url">Base URL</Label>
              <Input
                id="base-url"
                v-model.trim="baseUrl"
                type="url"
                class="font-mono sm:max-w-lg"
                placeholder="https://binstash.example.com"
                :disabled="savingDomain"
              />
            </div>

            <Button type="submit" :disabled="savingDomain">
              {{ savingDomain ? 'Saving…' : 'Save domain settings' }}
            </Button>
          </form>
        </section>
      </TabsContent>
    </Tabs>
  </div>
</template>
