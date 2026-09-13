import { mount } from '@vue/test-utils'
import { defineComponent, h, inject, provide, ref } from 'vue'
import type { Ref } from 'vue'
import { beforeEach, describe, expect, it, vi } from 'vitest'

import MoveRepositoryDialog from './MoveRepositoryDialog.vue'

/**
 * The dialog is worth testing because it is the only thing standing between an admin and an
 * irreversible cross-tenant move: it must not offer a destination the server would reject, and
 * it must not fire the mutation without an explicit choice.
 *
 * GraphQL is mocked at the composable boundary — these assertions are about the component's
 * rules, not about Apollo. The reka-ui `Select` and `Dialog` primitives are stubbed because both
 * render through a portal behind real pointer events, which jsdom does not provide; the stubs
 * keep the component's own props (`value`, `disabled`) observable.
 */

type MoveTarget = {
  tenantId: string
  tenantName: string
  tenantSlug: string
  storageClassName: string
  nameConflict: boolean
}

const targets = ref<MoveTarget[]>([])
const targetsLoading = ref(false)
const mutate = vi.fn()
const toastError = vi.fn()
const toastSuccess = vi.fn()

vi.mock('@/composables/useGraphql', () => ({
  useQuery: () => ({
    result: ref({ repositoryMoveTargets: targets.value }),
    loading: targetsLoading,
    error: ref(undefined),
    refetch: vi.fn(),
    fetchMore: vi.fn(),
  }),
  useMutation: () => ({ mutate, loading: ref(false), error: ref(undefined) }),
}))

vi.mock('vue-sonner', () => ({
  toast: {
    success: (...args: unknown[]) => toastSuccess(...args),
    error: (...args: unknown[]) => toastError(...args),
  },
}))

const SELECT_KEY = Symbol('select-stub')

const SelectStub = defineComponent({
  props: { modelValue: { type: String, default: undefined }, disabled: Boolean },
  emits: ['update:modelValue'],
  setup(_props, { emit, slots }) {
    provide(SELECT_KEY, (value: string) => emit('update:modelValue', value))
    return () => h('div', slots.default?.())
  },
})

const SelectItemStub = defineComponent({
  props: { value: { type: String, required: true }, disabled: Boolean },
  setup(props, { slots }) {
    const select = inject<(value: string) => void>(SELECT_KEY)!
    return () =>
      h(
        'button',
        {
          type: 'button',
          'data-testid': 'target',
          'data-value': props.value,
          disabled: props.disabled,
          onClick: () => select(props.value),
        },
        slots.default?.(),
      )
  },
})

const passthrough = (tag = 'div') =>
  defineComponent({ setup: (_p, { slots }) => () => h(tag, slots.default?.()) })

const stubs = {
  Dialog: passthrough(),
  DialogContent: passthrough(),
  DialogHeader: passthrough(),
  DialogTitle: passthrough(),
  DialogDescription: passthrough(),
  DialogFooter: passthrough(),
  Select: SelectStub,
  SelectTrigger: passthrough(),
  SelectValue: passthrough('span'),
  SelectContent: passthrough(),
  SelectItem: SelectItemStub,
}

function mountDialog() {
  return mount(MoveRepositoryDialog, {
    props: { open: true, repoId: 'repo-1', repoName: 'artifacts' },
    global: { stubs },
  })
}

const eligible: MoveTarget = {
  tenantId: 'tenant-b',
  tenantName: 'Team B',
  tenantSlug: 'team-b',
  storageClassName: 'archive',
  nameConflict: false,
}

const conflicting: MoveTarget = {
  tenantId: 'tenant-c',
  tenantName: 'Team C',
  tenantSlug: 'team-c',
  storageClassName: 'standard',
  nameConflict: true,
}

/** The "Move repository" button; the other footer button is Cancel. */
function moveButton(wrapper: ReturnType<typeof mountDialog>) {
  return wrapper
    .findAll('button')
    .find((button) => button.text().startsWith('Move repository'))!
}

describe('MoveRepositoryDialog', () => {
  beforeEach(() => {
    targets.value = []
    targetsLoading.value = false
    mutate.mockReset()
    mutate.mockResolvedValue({})
    toastError.mockReset()
    toastSuccess.mockReset()
  })

  it('disables confirmation until a destination is chosen', async () => {
    targets.value = [eligible]
    const wrapper = mountDialog()

    expect(moveButton(wrapper).attributes('disabled')).toBeDefined()

    await wrapper.get('[data-value="tenant-b"]').trigger('click')

    expect(moveButton(wrapper).attributes('disabled')).toBeUndefined()
  })

  it('offers a workspace whose name is taken, but does not let it be picked', async () => {
    targets.value = [eligible, conflicting]
    const wrapper = mountDialog()

    const items = wrapper.findAll('[data-testid="target"]')
    expect(items).toHaveLength(2)
    expect(wrapper.get('[data-value="tenant-c"]').attributes('disabled')).toBeDefined()
    expect(wrapper.get('[data-value="tenant-c"]').text()).toContain('name already taken')
  })

  it('warns that access is not carried over once a destination is chosen', async () => {
    targets.value = [eligible]
    const wrapper = mountDialog()

    expect(wrapper.text()).not.toContain('Access is not carried over')

    await wrapper.get('[data-value="tenant-b"]').trigger('click')

    expect(wrapper.text()).toContain('Access is not carried over')
    expect(wrapper.text()).toContain('Team B')
  })

  it('moves the repository and reports the destination back to the page', async () => {
    targets.value = [eligible]
    const wrapper = mountDialog()

    await wrapper.get('[data-value="tenant-b"]').trigger('click')
    await moveButton(wrapper).trigger('click')

    expect(mutate).toHaveBeenCalledWith({
      input: { repoId: 'repo-1', targetTenantId: 'tenant-b' },
    })
    expect(wrapper.emitted('moved')).toEqual([['tenant-b']])
    expect(wrapper.emitted('update:open')).toEqual([[false]])
    expect(toastSuccess).toHaveBeenCalled()
  })

  it('keeps the dialog open and surfaces the server message when the move is refused', async () => {
    targets.value = [eligible]
    mutate.mockRejectedValue({ errors: [{ message: 'A repository named ‘artifacts’ already exists.' }] })
    const wrapper = mountDialog()

    await wrapper.get('[data-value="tenant-b"]').trigger('click')
    await moveButton(wrapper).trigger('click')

    expect(wrapper.emitted('moved')).toBeUndefined()
    expect(wrapper.emitted('update:open')).toBeUndefined()
    expect(toastError).toHaveBeenCalledWith('A repository named ‘artifacts’ already exists.')
  })

  it('explains an empty destination list instead of showing an empty picker', () => {
    const wrapper = mountDialog()

    expect(wrapper.text()).toContain('There is no workspace to move this repository to')
  })

  it('explains that every eligible workspace already holds that name', () => {
    targets.value = [conflicting]
    const wrapper = mountDialog()

    expect(wrapper.text()).toContain('already holds a repository called')
  })
})
