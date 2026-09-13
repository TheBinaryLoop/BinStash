import { defineStore } from 'pinia'
import { computed, ref } from 'vue'

import { apiJson, apiPost, ApiError } from '@/lib/http'
import { resetApolloStore, setApolloTenant } from '@/lib/apollo'

export interface UserInfo {
  firstName: string
  middleName?: string | null
  lastName: string
  email: string
  isEmailConfirmed: boolean
  onboardingCompleted: boolean
  roles: string[]
}

export interface PublicInstanceConfig {
  instanceMode?: string | null
  tenancyMode?: string | null
}

export const useAuthStore = defineStore('auth', () => {
  const user = ref<UserInfo | null>(null)
  const instanceConfig = ref<PublicInstanceConfig | null>(null)
  /** Null until the first /manage/info round trip settles; guards must await `ready`. */
  const initialised = ref(false)
  let inflight: Promise<void> | null = null

  const isAuthenticated = computed(() => user.value !== null)
  const isEmailVerified = computed(() => user.value?.isEmailConfirmed === true)
  const isInstanceAdmin = computed(() => user.value?.roles.includes('InstanceAdmin') === true)
  const displayName = computed(() =>
    user.value ? `${user.value.firstName} ${user.value.lastName}`.trim() : '',
  )
  const isSingleTenant = computed(
    () => instanceConfig.value?.tenancyMode?.toLowerCase() === 'single',
  )

  /** Idempotent: concurrent callers (router guard + shell) share one request. */
  async function ready(): Promise<void> {
    if (initialised.value) return
    inflight ??= load().finally(() => {
      inflight = null
      initialised.value = true
    })
    return inflight
  }

  async function load(): Promise<void> {
    const [info, config] = await Promise.allSettled([
      apiJson<UserInfo>('/api/auth/manage/info'),
      apiJson<PublicInstanceConfig>('/api/instance/config'),
    ])

    // A 401 here is the normal signed-out path, not an error worth surfacing.
    user.value = info.status === 'fulfilled' ? info.value : null
    instanceConfig.value = config.status === 'fulfilled' ? config.value : null
  }

  async function refresh(): Promise<void> {
    try {
      user.value = await apiJson<UserInfo>('/api/auth/manage/info')
    } catch (error) {
      if (error instanceof ApiError && error.status === 401) user.value = null
      else throw error
    }
  }

  async function signIn(email: string, password: string, twoFactorCode?: string): Promise<void> {
    await apiPost('/api/auth/login?useCookies=true', {
      email,
      password,
      ...(twoFactorCode ? { twoFactorCode } : {}),
    })
    await refresh()
  }

  async function register(input: {
    firstName: string
    lastName: string
    middleName?: string
    email: string
    password: string
    invitationCode?: string
  }): Promise<void> {
    await apiPost('/api/auth/register', input)
  }

  async function signOut(): Promise<void> {
    try {
      await apiPost('/api/auth/logout', {})
    } finally {
      // Always clear locally: a failed logout must not leave cached tenant data behind.
      user.value = null
      setApolloTenant(null)
      await resetApolloStore()
    }
  }

  function forgotPassword(email: string) {
    return apiPost('/api/auth/forgotPassword', { email })
  }

  function resetPassword(email: string, resetCode: string, newPassword: string) {
    return apiPost('/api/auth/resetPassword', { email, resetCode, newPassword })
  }

  function confirmEmail(userId: string, code: string) {
    return apiJson(
      `/api/auth/confirmEmail?userId=${encodeURIComponent(userId)}&code=${encodeURIComponent(code)}`,
    )
  }

  function resendConfirmationEmail(email: string) {
    return apiPost('/api/auth/resendConfirmationEmail', { email })
  }

  return {
    user,
    instanceConfig,
    initialised,
    isAuthenticated,
    isEmailVerified,
    isInstanceAdmin,
    isSingleTenant,
    displayName,
    ready,
    refresh,
    signIn,
    register,
    signOut,
    forgotPassword,
    resetPassword,
    confirmEmail,
    resendConfirmationEmail,
  }
})
