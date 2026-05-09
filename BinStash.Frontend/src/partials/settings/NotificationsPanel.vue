<template>
  <div class="grow">

    <!-- Panel body -->
    <div class="p-6 space-y-6">
      <h2 class="text-2xl text-gray-800 dark:text-gray-100 font-bold mb-5">My Notifications</h2>

      <!-- General -->
      <section>
        <h3 class="text-xl leading-snug text-gray-800 dark:text-gray-100 font-bold mb-1">General</h3>
        <ul>
          <li class="flex justify-between items-center py-3 border-b border-gray-200 dark:border-gray-700/60">
            <!-- Left -->
            <div>
              <div class="text-gray-800 dark:text-gray-100 font-semibold">Comments and replies</div>
              <div class="text-sm">Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit.</div>
            </div>
            <!-- Right -->
            <div class="flex items-center ml-4">
              <div class="text-sm text-gray-400 dark:text-gray-500 italic mr-2">{{comments}}</div>
              <div class="form-switch">
                <input type="checkbox" id="comments" class="sr-only" v-model="comments" true-value="On" false-value="Off" />
                <label for="comments">
                  <span class="bg-white shadow-xs" aria-hidden="true"></span>
                  <span class="sr-only">Enable smart sync</span>
                </label>
              </div>
            </div>
          </li>
          <li class="flex justify-between items-center py-3 border-b border-gray-200 dark:border-gray-700/60">
            <!-- Left -->
            <div>
              <div class="text-gray-800 dark:text-gray-100 font-semibold">Messages</div>
              <div class="text-sm">Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit.</div>
            </div>
            <!-- Right -->
            <div class="flex items-center ml-4">
              <div class="text-sm text-gray-400 dark:text-gray-500 italic mr-2">{{messages}}</div>
              <div class="form-switch">
                <input type="checkbox" id="messages" class="sr-only" v-model="messages" true-value="On" false-value="Off" />
                <label for="messages">
                  <span class="bg-white shadow-xs" aria-hidden="true"></span>
                  <span class="sr-only">Enable smart sync</span>
                </label>
              </div>
            </div>
          </li>
          <li class="flex justify-between items-center py-3 border-b border-gray-200 dark:border-gray-700/60">
            <!-- Left -->
            <div>
              <div class="text-gray-800 dark:text-gray-100 font-semibold">Mentions</div>
              <div class="text-sm">Excepteur sint occaecat cupidatat non in culpa qui officia deserunt mollit.</div>
            </div>
            <!-- Right -->
            <div class="flex items-center ml-4">
              <div class="text-sm text-gray-400 dark:text-gray-500 italic mr-2">{{mentions}}</div>
              <div class="form-switch">
                <input type="checkbox" id="mentions" class="sr-only" v-model="mentions" true-value="On" false-value="Off" />
                <label for="mentions">
                  <span class="bg-white shadow-xs" aria-hidden="true"></span>
                  <span class="sr-only">Enable smart sync</span>
                </label>
              </div>
            </div>
          </li>
        </ul>
      </section>

      <!-- Shares -->
      <section>
        <h3 class="text-xl leading-snug text-gray-800 dark:text-gray-100 font-bold mb-1">Shares</h3>
        <ul>
          <li class="flex justify-between items-center py-3 border-b border-gray-200 dark:border-gray-700/60">
            <!-- Left -->
            <div>
              <div class="text-gray-800 dark:text-gray-100 font-semibold">Shares of my content</div>
              <div class="text-sm">Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit.</div>
            </div>
            <!-- Right -->
            <div class="flex items-center ml-4">
              <button class="btn-sm border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 shadow-xs">Manage</button>
            </div>
          </li>
          <li class="flex justify-between items-center py-3 border-b border-gray-200 dark:border-gray-700/60">
            <!-- Left -->
            <div>
              <div class="text-gray-800 dark:text-gray-100 font-semibold">Team invites</div>
              <div class="text-sm">Excepteur sint occaecat cupidatat non in culpa qui officia deserunt mollit.</div>
            </div>
            <!-- Right -->
            <div class="flex items-center ml-4">
              <button class="btn-sm border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 shadow-xs">Manage</button>
            </div>
          </li>
          <li class="flex justify-between items-center py-3 border-b border-gray-200 dark:border-gray-700/60">
            <!-- Left -->
            <div>
              <div class="text-gray-800 dark:text-gray-100 font-semibold">Smart connection</div>
              <div class="text-sm">Excepteur sint occaecat cupidatat non in culpa qui officia deserunt mollit.</div>
            </div>
            <!-- Right -->
            <div class="flex items-center ml-4">
              <div class="text-sm text-gray-400 dark:text-gray-500 italic mr-2 hidden md:block">Active</div>
              <button class="btn-sm border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 shadow-xs text-red-500">Disable</button>
            </div>
          </li>
        </ul>
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

export default {
  name: 'NotificationsPanel',
  setup() {

  const comments = ref('On')
  const messages = ref('On')
  const mentions = ref('Off')

    return {
      comments,
      messages,
      mentions,
    }
  }
}
</script>