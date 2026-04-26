<template>
  <div class="bg-white dark:bg-gray-800 shadow-xs rounded-xl px-5 py-4">
      <div class="md:flex justify-between items-center space-y-4 md:space-y-0 space-x-2">
          <!-- Left side -->
          <div class="flex items-start space-x-3 md:space-x-4">
              <div class="w-9 h-9 shrink-0 mt-1">
                  <img class="w-9 h-9 rounded-full" :src="item.image" width="36" height="36" :alt="item.company" />
              </div>
              <div>
                  <router-link class="inline-flex font-semibold text-gray-800 dark:text-gray-100" :to="item.link">{{item.role}}</router-link>
                  <div class="text-sm">{{item.details}}</div>
              </div>
          </div>
          <!-- Right side -->
          <div class="flex items-center space-x-4 pl-10 md:pl-0">
              <div class="text-sm text-gray-500 dark:text-gray-400 italic whitespace-nowrap">{{item.date}}</div>
              <div v-if="item.type" class="text-xs inline-flex font-medium rounded-full text-center px-2.5 py-1" :class="item.type === 'Featured' ? 'bg-yellow-500/20 text-yellow-700' : 'bg-green-500/20 text-green-700'">{{item.type}}</div>
              <button :class="item.fav ? 'text-yellow-500' : 'text-gray-300 dark:text-gray-600 hover:text-gray-400 dark:hover:text-gray-500'">
                  <span class="sr-only">Bookmark</span>
                  <svg class="w-3 h-4 fill-current" width="12" height="16" xmlns="http://www.w3.org/2000/svg">
                      <path d="M2 0C.9 0 0 .9 0 2v14l6-3 6 3V2c0-1.1-.9-2-2-2H2Z" />
                  </svg>
              </button>
          </div>
      </div>
  </div>
</template>

<script>
export default {
  name: 'JobListItem',
  props: ['item'],
}
</script>