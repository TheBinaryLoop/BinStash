<template>
  <div class="col-span-full sm:col-span-6 xl:col-span-3 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <div class="flex flex-col h-full">
      <!-- Card top -->
      <div class="grow p-5">
        <!-- Menu button -->
        <div class="relative">
          <EditMenu align="right" class="absolute top-0 right-0 inline-flex">
            <li>
              <router-link class="font-medium text-sm text-gray-600 dark:text-gray-300 hover:text-gray-800 dark:hover:text-gray-200 flex py-1 px-3" to="#0">Option 1</router-link>
            </li>
            <li>
              <router-link class="font-medium text-sm text-gray-600 dark:text-gray-300 hover:text-gray-800 dark:hover:text-gray-200 flex py-1 px-3" to="#0">Option 2</router-link>
            </li>
            <li>
              <router-link class="font-medium text-sm text-red-500 hover:text-red-600 flex py-1 px-3" to="#0">Remove</router-link>
            </li>
          </EditMenu>
        </div>
        <!-- Image + name -->
        <header>
          <div class="flex justify-center mb-2">
            <router-link class="relative inline-flex items-start" :to="item.link">
              <img class="rounded-full" :src="item.image" width="64" height="64" :alt="item.name" />
            </router-link>
          </div>
          <div class="text-center">
            <router-link class="inline-flex text-gray-800 dark:text-gray-100 hover:text-gray-900 dark:hover:text-white" :to="item.link">
              <h2 class="text-xl leading-snug justify-center font-semibold">{{item.name}}</h2>
            </router-link>
          </div>
          <div class="flex justify-center items-center">
            <span class="text-sm font-medium text-gray-400 dark:text-gray-500 -mt-0.5 mr-1">-&gt;</span> <span>{{item.location}}</span>
          </div>
        </header>
        <!-- Bio -->
        <!-- <div class="text-center mt-2">
          <div class="text-sm">{{item.content}}</div>
        </div> -->
      </div>
      <!-- Card footer -->
      <div class="border-t border-gray-100 dark:border-gray-700/60">
        <router-link class="block text-center text-sm text-violet-500 hover:text-violet-600 dark:hover:text-violet-400 font-medium px-3 py-4" :to="item.link">
          <div class="flex items-center justify-center">
            <svg class="fill-current shrink-0 mr-2" width="16" height="16" viewBox="0 0 16 16">
              <path d="M11.7.3c-.4-.4-1-.4-1.4 0l-10 10c-.2.2-.3.4-.3.7v4c0 .6.4 1 1 1h4c.3 0 .5-.1.7-.3l10-10c.4-.4.4-1 0-1.4l-4-4zM4.6 14H2v-2.6l6-6L10.6 8l-6 6zM12 6.6L9.4 4 11 2.4 13.6 5 12 6.6z" />
            </svg>
            <span>View Details</span>
          </div>
        </router-link>
      </div>
    </div>
  </div>
</template>

<script lang="ts">
import EditMenu from '../../components/DropdownEditMenu.vue'

export default {
  name: 'ChunkStoreCard',
  props: ['item'],
  components: {
    EditMenu,
  },  
}
</script>