<template>
  <div class="mt-4">
    <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase mb-3">Direct messages</div>
    <ul class="mb-6">
      <li class="-mx-2">
        <button class="flex items-center justify-between w-full p-2 rounded-lg bg-linear-to-r from-violet-500/[0.12] dark:from-violet-500/[0.24] to-violet-500/[0.04]" @click.stop="$emit('close-msgsidebar')">
          <div class="flex items-center truncate">
            <img class="w-8 h-8 rounded-full mr-2" src="../../images/user-32-01.jpg" width="32" height="32" alt="User 01" />
            <div class="truncate">
              <span class="text-sm font-medium text-gray-800 dark:text-gray-100">Dominik Lamakani</span>
            </div>
          </div>
          <div class="flex items-center ml-2">
            <div class="text-xs inline-flex font-medium bg-violet-400 text-white rounded-full text-center leading-5 px-2">2</div>
          </div>
        </button>
      </li>
      <li class="-mx-2">
        <button class="flex items-center justify-between w-full p-2 rounded-sm" @click.stop="$emit('close-msgsidebar')">
          <div class="flex items-center truncate">
            <img class="w-8 h-8 rounded-full mr-2" src="../../images/user-32-02.jpg" width="32" height="32" alt="User 02" />
            <div class="truncate">
              <span class="text-sm font-medium text-gray-800 dark:text-gray-100">Tisha Yanchev</span>
            </div>
          </div>
          <div class="flex items-center ml-2">
            <div class="text-xs inline-flex font-medium bg-violet-400 text-white rounded-full text-center leading-5 px-2">4</div>
          </div>
        </button>
      </li>
      <li class="-mx-2">
        <button class="flex items-center justify-between w-full p-2 rounded-sm" @click.stop="$emit('close-msgsidebar')">
          <div class="flex items-center truncate">
            <img class="w-8 h-8 rounded-full mr-2" src="../../images/user-32-03.jpg" width="32" height="32" alt="User 03" />
            <div class="truncate">
              <span class="text-sm font-medium text-gray-800 dark:text-gray-100">Jerzy Wierzy</span>
            </div>
          </div>
          <div class="flex items-center ml-2">
            <img class="rounded-full shrink-0" src="../../images/user-32-03.jpg" width="20" height="20" alt="User 03" />
          </div>
        </button>
      </li>
      <li class="-mx-2">
        <button class="flex items-center justify-between w-full p-2 rounded-sm" @click.stop="$emit('close-msgsidebar')">
          <div class="flex items-center truncate">
            <img class="w-8 h-8 rounded-full mr-2" src="../../images/user-32-04.jpg" width="32" height="32" alt="User 04" />
            <div class="truncate">
              <span class="text-sm font-medium text-gray-800 dark:text-gray-100">Adrian Przetocki</span>
            </div>
          </div>
          <div class="flex items-center ml-2">
            <svg class="w-3 h-3 shrink-0 fill-current text-gray-400" viewBox="0 0 12 12">
              <path d="M10.28 2.28L3.989 8.575 1.695 6.28A1 1 0 00.28 7.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 2.28z" />
            </svg>
          </div>
        </button>
      </li>
      <li class="-mx-2">
        <button class="flex items-center justify-between w-full p-2 rounded-sm" @click.stop="$emit('close-msgsidebar')">
          <div class="flex items-center truncate">
            <img class="w-8 h-8 rounded-full mr-2" src="../../images/user-32-05.jpg" width="32" height="32" alt="User 05" />
            <div class="truncate">
              <span class="text-sm font-medium text-gray-800 dark:text-gray-100">Simona Lürwer</span>
            </div>
          </div>
          <div class="flex items-center ml-2">
            <svg class="w-3 h-3 shrink-0 fill-current text-gray-400" viewBox="0 0 12 12">
              <path d="M10.28 2.28L3.989 8.575 1.695 6.28A1 1 0 00.28 7.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 2.28z" />
            </svg>
          </div>
        </button>
      </li>
      <li class="-mx-2">
        <button class="flex items-center justify-between w-full p-2 rounded-sm" @click.stop="$emit('close-msgsidebar')">
          <div class="flex items-center truncate">
            <img class="w-8 h-8 rounded-full mr-2" src="../../images/user-32-06.jpg" width="32" height="32" alt="User 06" />
            <div class="truncate">
              <span class="text-sm font-medium text-gray-800 dark:text-gray-100">Mary Roszczewski</span>
            </div>
          </div>
          <div class="flex items-center ml-2">
            <svg class="w-3 h-3 shrink-0 fill-current text-gray-400" viewBox="0 0 12 12">
              <path d="M10.28 2.28L3.989 8.575 1.695 6.28A1 1 0 00.28 7.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 2.28z" />
            </svg>
          </div>
        </button>
      </li>
    </ul>
  </div>
</template>

<script>
export default {
  name: 'DirectMessages',
  props: ['msgSidebarOpen'],
}
</script>