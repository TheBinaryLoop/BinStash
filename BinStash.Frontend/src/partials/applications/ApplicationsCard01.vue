<template>
  <div class="col-span-full sm:col-span-6 xl:col-span-3 bg-white dark:bg-gray-800 shadow-xs rounded-xl overflow-hidden">
    <div class="flex flex-col h-full">
      <!-- Image -->
      <img class="w-full" :src="item.image" width="286" height="160" :alt="item.title" />
      <!-- Card Content -->
      <div class="grow flex flex-col p-5">
        <!-- Card body -->
        <div class="grow">
          <!-- Header -->
          <header class="mb-2">
            <h3 class="text-lg text-gray-800 dark:text-gray-100 font-semibold mb-1">{{item.title}}</h3>
            <div class="text-sm">{{item.content}}</div>
          </header>
          <!-- Rating -->
          <div class="flex items-center space-x-2 mb-4">
            <!-- Stars -->
            <div class="flex space-x-1">
              <button>
                <span class="sr-only">1 star</span>
                <svg class="fill-current" :class="Math.round(item.rating) > 0 ? 'text-yellow-500' : 'text-gray-300'" width="16" height="16" viewBox="0 0 16 16">
                  <path d="M10 5.934L8 0 6 5.934H0l4.89 3.954L2.968 16 8 12.223 13.032 16 11.11 9.888 16 5.934z" />
                </svg>
              </button>
              <button>
                <span class="sr-only">2 stars</span>
                <svg class="fill-current" :class="Math.round(item.rating) > 1 ? 'text-yellow-500' : 'text-gray-300'" width="16" height="16" viewBox="0 0 16 16">
                  <path d="M10 5.934L8 0 6 5.934H0l4.89 3.954L2.968 16 8 12.223 13.032 16 11.11 9.888 16 5.934z" />
                </svg>
              </button>
              <button>
                <span class="sr-only">3 stars</span>
                <svg class="fill-current" :class="Math.round(item.rating) > 2 ? 'text-yellow-500' : 'text-gray-300'" width="16" height="16" viewBox="0 0 16 16">
                  <path d="M10 5.934L8 0 6 5.934H0l4.89 3.954L2.968 16 8 12.223 13.032 16 11.11 9.888 16 5.934z" />
                </svg>
              </button>
              <button>
                <span class="sr-only">4 stars</span>
                <svg class="fill-current" :class="Math.round(item.rating) > 3 ? 'text-yellow-500' : 'text-gray-300'" width="16" height="16" viewBox="0 0 16 16">
                  <path d="M10 5.934L8 0 6 5.934H0l4.89 3.954L2.968 16 8 12.223 13.032 16 11.11 9.888 16 5.934z" />
                </svg>
              </button>
              <button>
                <span class="sr-only">5 stars</span>
                <svg class="fill-current" :class="Math.round(item.rating) > 4 ? 'text-yellow-500' : 'text-gray-300'" width="16" height="16" viewBox="0 0 16 16">
                  <path d="M10 5.934L8 0 6 5.934H0l4.89 3.954L2.968 16 8 12.223 13.032 16 11.11 9.888 16 5.934z" />
                </svg>
              </button>
            </div>
            <!-- Rate -->
            <div class="inline-flex text-sm font-medium bg-yellow-500/20 text-yellow-700 rounded-full text-center px-2.5 py-0.5">{{item.rating}}</div>
          </div>
          <!-- Features list -->
          <ul class="text-sm space-y-2 mb-5 dark:text-gray-300">
            <li v-if="item.feat01" class="flex items-center">
              <svg class="fill-current text-gray-400 dark:text-gray-500 shrink-0 mr-3" width="16" height="16" viewBox="0 0 16 16">
                <path d="M7 11.674A1 1 0 0 1 7.739 10H8a1 1 0 0 1 1 1v1.699L14.034 8 9 3.302v1.699a1 1 0 0 1-1 1H2V8.27a1 1 0 1 1-2 0V6a2 2 0 0 1 2-2h5V2.153C7 .84 8.563.16 9.523 1.055l6.268 5.849a1.5 1.5 0 0 1 0 2.193l-6.267 5.849C8.565 15.84 7 15.16 7 13.849v-2.175Zm-1.278.878a.498.498 0 0 1 0 .896L4.2 14.2l-.752 1.521a.5.5 0 0 1-.896 0l-.752-1.52-1.521-.753a.498.498 0 0 1 0-.896l1.521-.752.752-1.52c.167-.342.728-.342.896 0L4.2 11.8l1.521.752ZM3.75 3a1.25 1.25 0 1 1 0-2.5 1.25 1.25 0 0 1 0 2.5Z" />
              </svg>
              <div>{{item.feat01}}</div>
            </li>
            <li v-if="item.feat02" class="flex items-center">
              <svg class="fill-current text-gray-400 dark:text-gray-500 shrink-0 mr-3" width="16" height="16" viewBox="0 0 16 16">
                <path d="M7 7a1 1 0 1 1 0 2H1a1 1 0 1 1 0-2h6Zm8-5a1 1 0 0 1 0 2H1a1 1 0 1 1 0-2h14ZM5 12a1 1 0 0 1 0 2H1a1 1 0 0 1 0-2h4Z" />
              </svg>
              <div>{{item.feat02}}</div>
            </li>
            <li v-if="item.feat03" class="flex items-center">
              <svg class="fill-current text-gray-400 dark:text-gray-500 shrink-0 mr-3" width="16" height="16" viewBox="0 0 16 16">
                <path d="M4.17 14H3a3 3 0 0 1-3-3V5a3 3 0 0 1 3-3h1.17A3.001 3.001 0 0 1 7 0h6a3 3 0 0 1 3 3v10a3 3 0 0 1-3 3H7a3.001 3.001 0 0 1-2.83-2ZM4 4H3a1 1 0 0 0-1 1v6a1 1 0 0 0 1 1h1V4Zm10 9V3a1 1 0 0 0-1-1H7a1 1 0 0 0-1 1v10a1 1 0 0 0 1 1h6a1 1 0 0 0 1-1Z" />
              </svg>
              <div>{{item.feat03}}</div>
            </li>
            <li v-if="item.feat04" class="flex items-center">
              <svg class="fill-current text-gray-400 dark:text-gray-500 shrink-0 mr-3" width="16" height="16" viewBox="0 0 16 16">
                <path d="M4.798 10.509a3.202 3.202 0 1 1 0-6.404 3.202 3.202 0 0 1 0 6.404Zm0-1.83a1.372 1.372 0 1 0 0-2.745 1.372 1.372 0 0 0 0 2.745Zm6.404-.915a3.202 3.202 0 1 1 0-6.404 3.202 3.202 0 0 1 0 6.404Zm0-1.83a1.372 1.372 0 1 0 0-2.744 1.372 1.372 0 0 0 0 2.744ZM9.335 11.11a.915.915 0 0 1-1.07-1.484 5.033 5.033 0 0 1 7.681 2.41.915.915 0 0 1-1.724.609 3.204 3.204 0 0 0-4.887-1.535Zm-7.558 4.28a.915.915 0 0 1-1.725-.61 5.033 5.033 0 0 1 9.49 0 .915.915 0 0 1-1.724.61 3.204 3.204 0 0 0-6.04 0Z" />
              </svg>
              <div>{{item.feat04}}</div>
            </li>
          </ul>
        </div>
        <!-- Card footer -->
        <div>
          <router-link class="btn-sm w-full bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white" :to="item.link">Install</router-link>
        </div>
      </div>
    </div>
  </div>
</template>

<script>
export default {
  name: 'ApplicationsCard01',
  props: ['item'],
}
</script>