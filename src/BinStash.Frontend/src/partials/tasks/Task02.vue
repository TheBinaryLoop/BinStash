<template>
  <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl p-4">
    <!-- Body -->
    <div class="mb-3">
      <!-- Title -->
      <h2 class="font-semibold text-gray-800 dark:text-gray-100 mb-1">User should receive a daily digest email</h2>
      <!-- Content -->
      <div>
        <div class="text-sm">Dedicated form for a category of users that will perform actions.</div>
      </div>
    </div>
    <!-- Footer -->
    <div class="flex items-center justify-between">
      <!-- Left side -->
      <button class="p-2 shrink-0 rounded-full bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 text-violet-500 transition">
        <span class="sr-only">Add</span>
        <svg class="w-3 h-3 fill-current" viewBox="0 0 12 12">
          <path d="M11 5H7V1a1 1 0 00-2 0v4H1a1 1 0 000 2h4v4a1 1 0 002 0V7h4a1 1 0 000-2z" />
        </svg>
      </button>
      <!-- Right side -->
      <div class="flex items-center">
        <!-- Date -->
        <div class="flex items-center text-yellow-500 ml-3">
          <svg class="shrink-0 fill-current mr-1.5" width="16" height="16" viewBox="0 0 16 16">
            <path d="M5 4a1 1 0 0 0 0 2h6a1 1 0 1 0 0-2H5Z" />
            <path d="M4 0a4 4 0 0 0-4 4v8a4 4 0 0 0 4 4h8a4 4 0 0 0 4-4V4a4 4 0 0 0-4-4H4ZM2 4a2 2 0 0 1 2-2h8a2 2 0 0 1 2 2v8a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V4Z" />
          </svg>
          <div class="text-sm text-yellow-600">Mar 27</div>
        </div>
        <!-- Replies button -->
        <button class="flex items-center text-gray-400 dark:text-gray-500 hover:text-violet-500  dark:hover:text-violet-500 ml-3">
          <svg class="shrink-0 fill-current mr-1.5" width="16" height="16" viewBox="0 0 16 16">
            <path d="M8 0C3.6 0 0 3.1 0 7s3.6 7 8 7h.6l5.4 2v-4.4c1.2-1.2 2-2.8 2-4.6 0-3.9-3.6-7-8-7zm4 10.8v2.3L8.9 12H8c-3.3 0-6-2.2-6-5s2.7-5 6-5 6 2.2 6 5c0 2.2-2 3.8-2 3.8z" />
          </svg>
          <div class="text-sm text-gray-500 dark:text-gray-400">6</div>
        </button>
        <!-- Attach button -->
        <button class="text-gray-400 dark:text-gray-500 hover:text-violet-500  dark:hover:text-violet-500 ml-3">
          <svg class="shrink-0 fill-current mr-1.5" width="16" height="16" viewBox="0 0 16 16">
            <path d="M11 0c1.3 0 2.6.5 3.5 1.5 1 .9 1.5 2.2 1.5 3.5 0 1.3-.5 2.6-1.4 3.5l-1.2 1.2c-.2.2-.5.3-.7.3-.2 0-.5-.1-.7-.3-.4-.4-.4-1 0-1.4l1.1-1.2c.6-.5.9-1.3.9-2.1s-.3-1.6-.9-2.2C12 1.7 10 1.7 8.9 2.8L7.7 4c-.4.4-1 .4-1.4 0-.4-.4-.4-1 0-1.4l1.2-1.1C8.4.5 9.7 0 11 0zM8.3 12c.4-.4 1-.5 1.4-.1.4.4.4 1 0 1.4l-1.2 1.2C7.6 15.5 6.3 16 5 16c-1.3 0-2.6-.5-3.5-1.5C.5 13.6 0 12.3 0 11c0-1.3.5-2.6 1.5-3.5l1.1-1.2c.4-.4 1-.4 1.4 0 .4.4.4 1 0 1.4L2.9 8.9c-.6.5-.9 1.3-.9 2.1s.3 1.6.9 2.2c1.1 1.1 3.1 1.1 4.2 0L8.3 12zm1.1-6.8c.4-.4 1-.4 1.4 0 .4.4.4 1 0 1.4l-4.2 4.2c-.2.2-.5.3-.7.3-.2 0-.5-.1-.7-.3-.4-.4-.4-1 0-1.4l4.2-4.2z" />
          </svg>
        </button>
      </div>
    </div>
  </div>
</template>

<script>
export default {
  name: 'Task02',
}
</script>