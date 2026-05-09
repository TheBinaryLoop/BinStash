<template>
  <div class="grow">
    <!-- Panel body -->
    <div class="p-6 space-y-6">
      <div>
        <h2 class="text-2xl text-gray-800 dark:text-gray-100 font-bold">Account Settings</h2>
        <p class="text-sm text-gray-500 dark:text-gray-400 mt-1">Manage your profile, security, and personal account preferences.</p>
      </div>

      <!-- Top-level tab bar (exact instance settings style) -->
      <div class="flex flex-wrap gap-1 mb-1 border-b border-gray-200 dark:border-gray-700">
        <button
          v-for="tab in TABS"
          :key="tab.id"
          @click="selectedTab = tab.id"
          class="px-4 py-2.5 text-sm font-medium border-b-2 -mb-px transition whitespace-nowrap"
          :class="selectedTab === tab.id
            ? 'border-violet-500 text-violet-600 dark:text-violet-400'
            : 'border-transparent text-gray-500 dark:text-gray-400 hover:text-gray-700 dark:hover:text-gray-200'"
        >
          <span class="flex items-center gap-2">
            <component :is="tab.icon" class="w-4 h-4" />
            {{ tab.label }}
          </span>
        </button>
      </div>

      <!-- Profile tab -->
      <section v-if="selectedTab === 'profile'" class="space-y-6">
        <div class="pb-6 border-b border-gray-200 dark:border-gray-700/60">
          <h3 class="text-xl leading-snug text-gray-800 dark:text-gray-100 font-bold mb-1">Profile</h3>
          <p class="text-sm text-gray-500 dark:text-gray-400">Manage your personal information and public profile details.</p>

          <div class="flex items-center mt-5">
            <div class="mr-4">
              <img class="w-16 h-16 rounded-full" src="../../images/user-avatar-80.png" width="64" height="64" alt="User avatar" />
            </div>
            <div class="flex gap-2">
              <button class="btn-sm dark:bg-gray-800 border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 text-gray-800 dark:text-gray-300">Upload</button>
              <button class="btn-sm dark:bg-gray-800 border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 text-gray-800 dark:text-gray-300">Remove</button>
            </div>
          </div>

          <div class="grid grid-cols-1 md:grid-cols-2 gap-4 mt-5">
            <div>
              <label class="block text-sm font-medium mb-1" for="first-name">First name</label>
              <input id="first-name" class="form-input w-full" type="text" v-model="profile.firstName" />
            </div>
            <div>
              <label class="block text-sm font-medium mb-1" for="last-name">Last name</label>
              <input id="last-name" class="form-input w-full" type="text" v-model="profile.lastName" />
            </div>
            <div class="md:col-span-2">
              <label class="block text-sm font-medium mb-1" for="display-name">Display name</label>
              <input id="display-name" class="form-input w-full" type="text" v-model="profile.displayName" />
            </div>
          </div>
        </div>

        <div>
          <h3 class="text-xl leading-snug text-gray-800 dark:text-gray-100 font-bold mb-1">Contact & Identity</h3>
          <p class="text-sm text-gray-500 dark:text-gray-400">Keep your login email and timezone preferences up to date.</p>

          <div class="grid grid-cols-1 md:grid-cols-2 gap-4 mt-5">
            <div>
              <label class="block text-sm font-medium mb-1" for="email">Email address</label>
              <input id="email" class="form-input w-full" type="email" v-model="contact.email" />
              <p class="text-xs text-emerald-600 dark:text-emerald-400 mt-1">Verified</p>
            </div>
            <div>
              <label class="block text-sm font-medium mb-1" for="timezone">Timezone</label>
              <select id="timezone" class="form-select w-full" v-model="contact.timezone">
                <option value="America/Los_Angeles">America/Los_Angeles</option>
                <option value="America/New_York">America/New_York</option>
                <option value="UTC">UTC</option>
                <option value="Europe/Berlin">Europe/Berlin</option>
              </select>
            </div>
          </div>
        </div>
      </section>

      <!-- Security tab -->
      <section v-else-if="selectedTab === 'security'">
        <h3 class="text-xl leading-snug text-gray-800 dark:text-gray-100 font-bold mb-1">Authentication & Security</h3>
        <p class="text-sm text-gray-500 dark:text-gray-400">Strengthen account security with MFA and session management.</p>

        <div class="mt-5 space-y-4">
          <div class="flex flex-wrap items-center justify-between gap-3 p-4 rounded-lg border border-gray-200 dark:border-gray-700/60">
            <div>
              <div class="font-semibold text-gray-800 dark:text-gray-100">Password</div>
              <div class="text-sm text-gray-500 dark:text-gray-400">Last changed 42 days ago.</div>
            </div>
            <button class="btn-sm dark:bg-gray-800 border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 text-gray-800 dark:text-gray-300">Change password</button>
          </div>

          <div class="flex flex-wrap items-center justify-between gap-3 p-4 rounded-lg border border-gray-200 dark:border-gray-700/60">
            <div>
              <div class="font-semibold text-gray-800 dark:text-gray-100">Two-factor authentication (MFA)</div>
              <div class="text-sm text-gray-500 dark:text-gray-400">Add an extra verification step when signing in.</div>
            </div>
            <div class="flex items-center gap-3">
              <span class="text-xs font-medium px-2 py-1 rounded bg-amber-100 dark:bg-amber-500/10 text-amber-700 dark:text-amber-400">
                {{ mfaEnabled ? 'Enabled' : 'Disabled' }}
              </span>
              <div class="form-switch">
                <input id="mfa" type="checkbox" class="sr-only" v-model="mfaEnabled" />
                <label for="mfa">
                  <span class="bg-white shadow-xs" aria-hidden="true"></span>
                  <span class="sr-only">Toggle MFA</span>
                </label>
              </div>
            </div>
          </div>

          <div class="flex flex-wrap items-center justify-between gap-3 p-4 rounded-lg border border-gray-200 dark:border-gray-700/60">
            <div>
              <div class="font-semibold text-gray-800 dark:text-gray-100">Active sessions</div>
              <div class="text-sm text-gray-500 dark:text-gray-400">Signed in on 3 devices.</div>
            </div>
            <button class="btn-sm border-red-200 dark:border-red-500/30 hover:border-red-300 dark:hover:border-red-500/50 text-red-600 dark:text-red-400">
              Sign out other sessions
            </button>
          </div>
        </div>
      </section>

      <!-- Preferences tab -->
      <section v-else-if="selectedTab === 'preferences'">
        <h3 class="text-xl leading-snug text-gray-800 dark:text-gray-100 font-bold mb-1">Preferences</h3>
        <p class="text-sm text-gray-500 dark:text-gray-400">Customize how your account behaves and how you receive updates.</p>

        <div class="grid grid-cols-1 md:grid-cols-2 gap-6 mt-5">
          <div>
            <label class="block text-sm font-medium mb-1" for="theme">Theme</label>
            <select id="theme" class="form-select w-full" v-model="preferences.theme">
              <option value="system">System</option>
              <option value="light">Light</option>
              <option value="dark">Dark</option>
            </select>
          </div>
          <div>
            <label class="block text-sm font-medium mb-1" for="language">Language</label>
            <select id="language" class="form-select w-full" v-model="preferences.language">
              <option value="en-US">English (US)</option>
              <option value="en-GB">English (UK)</option>
              <option value="de-DE">Deutsch</option>
            </select>
          </div>
        </div>

        <ul class="mt-5">
          <li class="flex justify-between items-center py-3 border-b border-gray-200 dark:border-gray-700/60">
            <div>
              <div class="text-gray-800 dark:text-gray-100 font-semibold">Product updates</div>
              <div class="text-sm text-gray-500 dark:text-gray-400">Release notes and feature announcements.</div>
            </div>
            <div class="form-switch">
              <input id="updates" type="checkbox" class="sr-only" v-model="preferences.productUpdates" />
              <label for="updates">
                <span class="bg-white shadow-xs" aria-hidden="true"></span>
                <span class="sr-only">Toggle product updates</span>
              </label>
            </div>
          </li>
          <li class="flex justify-between items-center py-3">
            <div>
              <div class="text-gray-800 dark:text-gray-100 font-semibold">Security alerts</div>
              <div class="text-sm text-gray-500 dark:text-gray-400">Important account and sign-in notifications.</div>
            </div>
            <div class="form-switch">
              <input id="security-alerts" type="checkbox" class="sr-only" v-model="preferences.securityAlerts" />
              <label for="security-alerts">
                <span class="bg-white shadow-xs" aria-hidden="true"></span>
                <span class="sr-only">Toggle security alerts</span>
              </label>
            </div>
          </li>
        </ul>
      </section>

      <!-- Connected tab -->
      <section v-else-if="selectedTab === 'connected'">
        <h3 class="text-xl leading-snug text-gray-800 dark:text-gray-100 font-bold mb-1">Connected Accounts</h3>
        <p class="text-sm text-gray-500 dark:text-gray-400">Manage identity providers linked to your account.</p>

        <ul class="mt-5 space-y-3">
          <li
            v-for="provider in providers"
            :key="provider.id"
            class="flex items-center justify-between p-4 rounded-lg border border-gray-200 dark:border-gray-700/60"
          >
            <div>
              <div class="font-semibold text-gray-800 dark:text-gray-100">{{ provider.label }}</div>
              <div class="text-sm" :class="provider.connected ? 'text-emerald-600 dark:text-emerald-400' : 'text-gray-500 dark:text-gray-400'">
                {{ provider.connected ? 'Connected' : 'Not connected' }}
              </div>
            </div>
            <button
              class="btn-sm"
              :class="provider.connected
                ? 'border-red-200 dark:border-red-500/30 hover:border-red-300 dark:hover:border-red-500/50 text-red-600 dark:text-red-400'
                : 'dark:bg-gray-800 border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 text-gray-800 dark:text-gray-300'"
            >
              {{ provider.connected ? 'Disconnect' : 'Connect' }}
            </button>
          </li>
        </ul>
      </section>

      <!-- Danger tab -->
      <section v-else>
        <h3 class="text-xl leading-snug text-red-600 dark:text-red-400 font-bold mb-1">Danger Zone</h3>
        <p class="text-sm text-gray-500 dark:text-gray-400">Permanent actions that affect your account and data.</p>

        <div class="mt-5 p-4 rounded-lg border border-red-200 dark:border-red-500/30 bg-red-50 dark:bg-red-500/10 flex flex-wrap items-center justify-between gap-3">
          <div>
            <div class="font-semibold text-red-700 dark:text-red-300">Delete account</div>
            <div class="text-sm text-red-600/90 dark:text-red-300/80">This action is irreversible and will remove your user profile.</div>
          </div>
          <button class="btn-sm border-red-300 dark:border-red-500/40 hover:border-red-400 dark:hover:border-red-500/70 text-red-700 dark:text-red-300">
            Request deletion
          </button>
        </div>
      </section>
    </div>

    <!-- Panel footer -->
    <footer>
      <div class="flex flex-col px-6 py-5 border-t border-gray-200 dark:border-gray-700/60">
        <div class="flex self-end">
          <button class="btn dark:bg-gray-800 border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 text-gray-800 dark:text-gray-300">Cancel</button>
          <button class="btn bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white ml-3">Save Changes</button>
        </div>
      </div>
    </footer>
  </div>  
</template>

<script>
import { ref } from 'vue'
import {
  IconUser,
  IconShieldLock,
  IconAdjustments,
  IconPlugConnected,
  IconAlertTriangle,
} from '@tabler/icons-vue'

export default {
  name: 'AccountPanel',
  components: {
    IconUser,
    IconShieldLock,
    IconAdjustments,
    IconPlugConnected,
    IconAlertTriangle,
  },
  setup() {

    const TABS = [
      { id: 'profile', label: 'Profile', icon: IconUser },
      { id: 'security', label: 'Security', icon: IconShieldLock },
      { id: 'preferences', label: 'Preferences', icon: IconAdjustments },
      { id: 'connected', label: 'Connected Accounts', icon: IconPlugConnected },
      { id: 'danger', label: 'Danger Zone', icon: IconAlertTriangle },
    ]

    const selectedTab = ref('profile')

    const profile = ref({
      firstName: 'Lana',
      lastName: 'Essmann',
      displayName: 'Lana E.',
    })

    const contact = ref({
      email: 'lana@example.com',
      timezone: 'America/Los_Angeles',
    })

    const mfaEnabled = ref(false)

    const preferences = ref({
      theme: 'system',
      language: 'en-US',
      productUpdates: true,
      securityAlerts: true,
    })

    const providers = ref([
      { id: 'google', label: 'Google', connected: true },
      { id: 'github', label: 'GitHub', connected: false },
      { id: 'entra', label: 'Entra ID', connected: false },
    ])

    return {
      TABS,
      selectedTab,
      profile,
      contact,
      mfaEnabled,
      preferences,
      providers,
    }
  }
}
</script>