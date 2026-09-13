<script setup lang="ts">
import { Globe, Mail, Send, Trash2, Users } from '@lucide/vue'
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
import { Switch } from '@/components/ui/switch'
import { Tabs, TabsContent, TabsList, TabsTrigger } from '@/components/ui/tabs'
import { useMutation, useQuery } from '@/composables/useGraphql'
import {
  DomainConfigDocument,
  EmailConfigDocument,
  GcConfigDocument,
  SendTestEmailDocument,
  SetDomainConfigDocument,
  SetEmailConfigDocument,
  SetGcConfigDocument,
  SetTenancyConfigDocument,
  TenancyConfigDocument,
} from '@/graphql/generated'
import { errorMessage } from '@/lib/errors'
import { formatRelative } from '@/lib/format'

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

/* ---- garbage collection ------------------------------------------------ */

const gc = useQuery(GcConfigDocument, {})
const { mutate: saveGc, loading: savingGc } = useMutation(SetGcConfigDocument)

const gcEnabled = ref(false)
const gcIntervalHours = ref(24)
const gcRetentionHours = ref(24)
const gcDryRun = ref(false)
const gcSkipReclaim = ref(false)
const gcWindowed = ref(false)
const gcWindowStart = ref(22)
const gcWindowEnd = ref(4)

watch(
  () => gc.result.value?.gcConfig,
  (config) => {
    if (!config) return
    gcEnabled.value = config.enabled
    gcIntervalHours.value = Math.round(config.intervalHours)
    gcRetentionHours.value = Math.round(config.retentionHours)
    gcDryRun.value = config.dryRun
    gcSkipReclaim.value = config.skipReclaim
    // The server models "no window" as either bound being absent; the form models it as a
    // toggle, so that the hour inputs can keep sensible values while the window is off.
    gcWindowed.value = config.windowStartHourUtc != null && config.windowEndHourUtc != null
    gcWindowStart.value = config.windowStartHourUtc ?? 22
    gcWindowEnd.value = config.windowEndHourUtc ?? 4
  },
  { immediate: true },
)

const HOURS = Array.from({ length: 24 }, (_, hour) => hour)

function hourLabel(hour: number): string {
  return `${hour.toString().padStart(2, '0')}:00`
}

/** Restates the schedule as a sentence, so the consequence is legible without arithmetic. */
const gcSummary = computed(() => {
  if (!gcEnabled.value) return 'Collection runs only when an admin starts one by hand.'

  const cadence =
    gcIntervalHours.value === 24
      ? 'Every chunk store is collected once a day'
      : `Every chunk store is collected every ${gcIntervalHours.value} hours`

  const when = gcWindowed.value
    ? `, starting between ${hourLabel(gcWindowStart.value)} and ${hourLabel(gcWindowEnd.value)} UTC`
    : ''

  if (gcDryRun.value) return `${cadence}${when}, reporting only — nothing is changed.`
  if (gcSkipReclaim.value)
    return `${cadence}${when}, quarantining unreachable content but never destroying it.`

  return `${cadence}${when}. Content quarantined more than ${gcRetentionHours.value} hours ago is destroyed.`
})

/**
 * The server reports the next instant a run may start, which is simply "now" whenever the window
 * is already open. Rendering that as a relative time produces "16 seconds ago" — a past tense for
 * something that has not happened, which reads as a missed run rather than a ready one.
 */
const gcNextEligible = computed(() => {
  const at = gc.result.value?.gcConfig?.nextEligibleAt
  if (!at) return null
  return new Date(at).getTime() <= Date.now() ? 'now' : formatRelative(at)
})

async function submitGc() {
  try {
    await saveGc({
      input: {
        enabled: gcEnabled.value,
        intervalHours: gcIntervalHours.value,
        retentionHours: gcRetentionHours.value,
        dryRun: gcDryRun.value,
        skipReclaim: gcSkipReclaim.value,
        clearWindow: !gcWindowed.value,
        windowStartHourUtc: gcWindowed.value ? gcWindowStart.value : undefined,
        windowEndHourUtc: gcWindowed.value ? gcWindowEnd.value : undefined,
      },
    })
    await gc.refetch()
    toast.success('Collection schedule saved.')
  } catch (caught) {
    toast.error(errorMessage(caught, 'Could not save the collection schedule.'))
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
        <TabsTrigger value="gc" class="gap-1.5">
          <Trash2 class="size-3.5" />
          Collection
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

      <TabsContent value="gc" class="space-y-4">
        <section class="bg-card hairline space-y-5 rounded-lg p-5">
          <div>
            <h2 class="text-sm font-medium">Scheduled collection</h2>
            <p class="text-muted-foreground text-xs">
              Reclaims space held by content no release refers to any more — orphans left by
              uploads that never finished. Stores stay online throughout.
            </p>
          </div>

          <form class="space-y-5" @submit.prevent="submitGc">
            <div class="flex items-start justify-between gap-4 sm:max-w-lg">
              <div class="space-y-0.5">
                <Label for="gc-enabled">Run collection automatically</Label>
                <p class="text-muted-foreground text-xs">
                  Off by default. Collection destroys unreachable content once its quarantine
                  expires, so it is opt-in rather than inherited from an upgrade.
                </p>
              </div>
              <Switch id="gc-enabled" v-model="gcEnabled" :disabled="savingGc" />
            </div>

            <div class="grid gap-4 sm:max-w-lg sm:grid-cols-2" :class="gcEnabled ? '' : 'opacity-50'">
              <div class="space-y-2">
                <Label for="gc-interval">Collect every</Label>
                <div class="flex items-center gap-2">
                  <Input
                    id="gc-interval"
                    v-model.number="gcIntervalHours"
                    type="number"
                    min="1"
                    max="8760"
                    class="font-mono"
                    :disabled="savingGc || !gcEnabled"
                  />
                  <span class="text-muted-foreground text-xs">hours</span>
                </div>
              </div>

              <div class="space-y-2">
                <Label for="gc-retention">Keep recoverable for</Label>
                <div class="flex items-center gap-2">
                  <Input
                    id="gc-retention"
                    v-model.number="gcRetentionHours"
                    type="number"
                    min="1"
                    max="8760"
                    class="font-mono"
                    :disabled="savingGc"
                  />
                  <span class="text-muted-foreground text-xs">hours</span>
                </div>
                <p class="text-muted-foreground text-xs">
                  Applies to manual runs too. Must outlast your longest upload.
                </p>
              </div>
            </div>

            <div class="space-y-3 sm:max-w-lg" :class="gcEnabled ? '' : 'opacity-50'">
              <div class="flex items-start justify-between gap-4">
                <div class="space-y-0.5">
                  <Label for="gc-windowed">Only start during set hours</Label>
                  <p class="text-muted-foreground text-xs">
                    Collection is sustained disk I/O on the same volumes that serve uploads and
                    downloads. A run already in progress is never interrupted at the window's end.
                  </p>
                </div>
                <Switch id="gc-windowed" v-model="gcWindowed" :disabled="savingGc || !gcEnabled" />
              </div>

              <div v-if="gcWindowed" class="grid grid-cols-2 gap-4">
                <div class="space-y-2">
                  <Label for="gc-window-start">From (UTC)</Label>
                  <Select v-model="gcWindowStart" :disabled="savingGc || !gcEnabled">
                    <SelectTrigger id="gc-window-start">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="hour in HOURS" :key="hour" :value="hour">
                        {{ hourLabel(hour) }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </div>
                <div class="space-y-2">
                  <Label for="gc-window-end">Until (UTC)</Label>
                  <Select v-model="gcWindowEnd" :disabled="savingGc || !gcEnabled">
                    <SelectTrigger id="gc-window-end">
                      <SelectValue />
                    </SelectTrigger>
                    <SelectContent>
                      <SelectItem v-for="hour in HOURS" :key="hour" :value="hour">
                        {{ hourLabel(hour) }}
                      </SelectItem>
                    </SelectContent>
                  </Select>
                </div>
              </div>
            </div>

            <div class="space-y-3 sm:max-w-lg" :class="gcEnabled ? '' : 'opacity-50'">
              <div class="flex items-start justify-between gap-4">
                <div class="space-y-0.5">
                  <Label for="gc-dry-run">Report only</Label>
                  <p class="text-muted-foreground text-xs">
                    Scheduled runs measure what they would collect and change nothing. The honest
                    way to watch a real workload before letting collection bite.
                  </p>
                </div>
                <Switch id="gc-dry-run" v-model="gcDryRun" :disabled="savingGc || !gcEnabled" />
              </div>

              <div class="flex items-start justify-between gap-4" :class="gcDryRun ? 'opacity-50' : ''">
                <div class="space-y-0.5">
                  <Label for="gc-skip-reclaim">Quarantine only</Label>
                  <p class="text-muted-foreground text-xs">
                    Hide unreachable content from deduplication but never destroy it. Reclaiming
                    then stays a manual step.
                  </p>
                </div>
                <Switch
                  id="gc-skip-reclaim"
                  v-model="gcSkipReclaim"
                  :disabled="savingGc || !gcEnabled || gcDryRun"
                />
              </div>
            </div>

            <Alert>
              <AlertDescription>
                {{ gcSummary }}
                <template v-if="gcEnabled && gcNextEligible">
                  Next eligible {{ gcNextEligible }}.
                </template>
              </AlertDescription>
            </Alert>

            <Button type="submit" :disabled="savingGc">
              {{ savingGc ? 'Saving…' : 'Save collection schedule' }}
            </Button>
          </form>
        </section>
      </TabsContent>
    </Tabs>
  </div>
</template>
