import { defineStore } from 'pinia'
import { apiFetch, throwForStatus } from '../shared/api/http'

type User = {
  id: string
  firstName: string
  middleName?: string
  lastName: string
  email: string
  isEmailConfirmed: boolean
  onboardingCompleted: boolean
  roles: string[]
}

export const useAuthStore = defineStore('auth', {
  state: () => ({
    user: null as User | null,
    isRestoring: false,
  }),
  getters: {
    isAuthenticated: (s) => !!s.user,
    isInstanceAdmin: (s) => s.user?.roles?.includes('InstanceAdmin') ?? false,
  },
  actions: {
    async login(email: string, password: string, staySignedIn: boolean) {
      const useSessionCookies = staySignedIn ? 'false' : 'true'
      const loginUrl = `/api/auth/login?useCookies=true&useSessionCookies=${useSessionCookies}`

      const res = await apiFetch(loginUrl, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ email, password }),
      })

      // throwForStatus handles both problem+json and plain error responses,
      // throwing an ApiError with a user-readable message on failure.
      await throwForStatus(res)

      // If login returns user info inline, use it directly. Otherwise fetch /me.
      try {
        const data = await res.json()
        if (data && typeof data === 'object' && 'email' in data) {
          this.user = data as User
          return
        }
      } catch {
        // ignore body parse errors — body may have been empty
      }

      await this.restore()
      if (!this.user) throw new Error('Login succeeded but user could not be loaded.')
    },

    logout() {
      this.user = null
      return fetch('/api/auth/logout?useCookies=true', {
        method: 'POST',
        credentials: 'include',
      })
    },

    async restore() {
      this.isRestoring = true
      try {
        const res = await apiFetch('/api/auth/manage/info', { method: 'GET' })
        
        // throwForStatus handles both problem+json and plain error responses,
        // throwing an ApiError with a user-readable message on failure.
        //await throwForStatus(res)

        if (!res.ok) {
          this.user = null
          return
        }
        this.user = (await res.json()) as User
      } finally {
        this.isRestoring = false
      }
    },
  },
})