<template>
  <div class="flex flex-col col-span-full xl:col-span-4 bg-linear-[225deg,var(--color-gray-800),var(--color-gray-900)] shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-700/60 flex items-center">
      <h2 class="font-semibold text-gray-200">Active Cards</h2>
    </header>
    <div class="h-full flex flex-col px-5 py-6">
      <!-- CC container -->
      <div class="relative w-full max-w-sm mx-auto bg-gray-700/50 p-2.5 rounded-2xl">
        <!-- Credit Card -->
        <div class="relative aspect-7/4 bg-linear-to-tr from-gray-900 to-gray-800 p-5 rounded-xl overflow-hidden">
          <div class="relative h-full flex flex-col justify-between">
            <!-- Logo on card -->
            <svg width="32" height="32" viewBox="0 0 32 32" xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink">
              <defs>
                <linearGradient x1="50%" y1="0%" x2="50%" y2="100%" id="icon1-b">
                  <stop stop-color="#B7ACFF" offset="0%" />
                  <stop stop-color="#E6E1FF" offset="100%" />
                </linearGradient>
                <linearGradient x1="50%" y1="24.537%" x2="50%" y2="100%" id="icon1-c">
                  <stop stop-color="#4634B1" offset="0%" />
                  <stop stop-color="#4634B1" stop-opacity="0" offset="100%" />
                </linearGradient>
                <path id="icon1-a" d="M16 0l16 32-16-5-16 5z" />
              </defs>
              <g transform="rotate(90 16 16)" fill="none" fill-rule="evenodd">
                <mask id="icon1-d" fill="#fff">
                  <use xlink:href="#icon1-a" />
                </mask>
                <use fill="url(#icon1-b)" xlink:href="#icon1-a" />
                <path fill="url(#icon1-c)" mask="url(#icon1-d)" d="M16-6h20v38H16z" />
              </g>
            </svg>
            <!-- Card number -->
            <div class="flex justify-between text-lg font-bold text-gray-200 tracking-widest drop-shadow-md">
              <span>****</span>
              <span>****</span>
              <span>****</span>
              <span>7328</span>
            </div>
            <!-- Card footer -->
            <div class="relative flex justify-between items-center z-10 mb-0.5">
              <!-- Card expiration -->
              <div class="text-sm font-bold text-gray-200 tracking-widest drop-shadow-md space-x-3">
                <span>EXP 12/24</span>
                <span>CVC ***</span>
              </div>
            </div>
            <!-- Mastercard logo -->
            <svg class="absolute bottom-0 right-0" width="48" height="28" viewBox="0 0 48 28">
              <circle fill="#F0BB33" cx="34" cy="14" r="14" fill-opacity=".8" />
              <circle fill="#FF5656" cx="14" cy="14" r="14" fill-opacity=".8" />
            </svg>
          </div>
        </div>
        <!-- Options button -->
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
      <!-- Details -->
      <div class="grow flex flex-col justify-center mt-3">
        <div class="text-xs text-gray-500 font-semibold uppercase mb-3">Details</div>
        <div class="space-y-2">
          <div>
            <div class="flex justify-between text-sm mb-2">
              <div class="text-gray-300">Payment Limits</div>
              <div class="text-gray-400 italic">$780,00 <span class="text-gray-500 dark:text-gray-400">/</span> $1,500.00</div>
            </div>
            <div class="relative w-full h-2 bg-gray-600 rounded-sm">
              <div class="absolute inset-0 bg-green-400 rounded-full" aria-hidden="true" style="width: 50%;"></div>
            </div>
          </div>
          <div>
            <div class="flex justify-between text-sm mb-2">
              <div class="text-gray-300">ATM Limits</div>
              <div class="text-gray-400 italic">$179,00 <span class="text-gray-500 dark:text-gray-400">/</span> $1,000.00</div>
            </div>
            <div class="relative w-full h-2 bg-gray-600 rounded-sm">
              <div class="absolute inset-0 bg-green-400 rounded-full" aria-hidden="true" style="width: 35%;"></div>
            </div>
          </div>
        </div>
      </div>
    </div>
  </div>
</template>

<script>
import EditMenu from '../../components/DropdownEditMenuCard.vue'

export default {
  name: 'FintechCard02',
  components: {
    EditMenu,
  },
}
</script>