import { mount } from '@vue/test-utils'
import { describe, expect, it } from 'vitest'

import QuotaAlert from './QuotaAlert.vue'
import type { QuotaUsage } from '@/lib/quota'

/**
 * This banner is the only thing that tells a workspace why its CI started returning 402, so the
 * states that matter are the ones a healthy dev instance can never produce: the open-source
 * billing default allows everything and reports no ceiling, so nothing short of a commercial
 * plugin would render anything but the empty case. Hence mounting it directly.
 *
 * `RouterLink` is stubbed because the component is mounted without a router; the assertions here
 * are about which message is shown, not about navigation.
 */
const stubs = { RouterLink: { template: '<a><slot /></a>' } }

const usage = (over: Partial<QuotaUsage> = {}): QuotaUsage => ({
  logicalBytes: 100,
  maxStorageBytes: 1_000,
  isLimited: true,
  isStorageAllowed: true,
  isIngestAllowed: true,
  isEgressAllowed: true,
  ...over,
})

function render(over: Partial<QuotaUsage> = {}) {
  return mount(QuotaAlert, { props: { usage: usage(over) }, global: { stubs } })
}

describe('QuotaAlert', () => {
  it('renders nothing while there is nothing to warn about', () => {
    expect(render().find('[role="alert"]').exists()).toBe(false)
  })

  it('renders nothing when usage has not loaded yet', () => {
    const wrapper = mount(QuotaAlert, { props: { usage: null }, global: { stubs } })
    expect(wrapper.find('[role="alert"]').exists()).toBe(false)
  })

  it('warns in amber when approaching the ceiling', () => {
    const wrapper = render({ logicalBytes: 850 })
    const alert = wrapper.find('[role="alert"]')

    expect(alert.exists()).toBe(true)
    expect(alert.classes().join(' ')).toContain('text-warning')
    expect(wrapper.text()).toContain('Approaching the storage quota')
  })

  it('escalates to destructive once the quota is reached', () => {
    const wrapper = render({ logicalBytes: 1_000 })
    const alert = wrapper.find('[role="alert"]')

    expect(alert.classes().join(' ')).toContain('text-destructive')
    expect(wrapper.text()).toContain('Storage quota reached')
  })

  it('names the blocked operations and the status code they fail with', () => {
    const wrapper = render({ isIngestAllowed: false })

    expect(wrapper.text()).toContain('Uploads are blocked')
    // The workspace should be able to match this to what it sees in a build log.
    expect(wrapper.text()).toContain('402')
  })

  it('shows remaining headroom when there is a ceiling to measure against', () => {
    expect(render({ logicalBytes: 900 }).text()).toContain('100 B left')
  })

  it('omits headroom for a blocked workspace, where it is not the reason', () => {
    const wrapper = render({ logicalBytes: 100, isEgressAllowed: false })

    expect(wrapper.text()).not.toContain('left.')
  })
})
