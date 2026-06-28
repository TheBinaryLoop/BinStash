<template>
  <div class="grow">

    <!-- Panel body -->
    <div class="p-6 space-y-6">

      <!-- Plans -->
      <section>
        <div class="mb-8">
          <h2 class="text-2xl text-gray-800 dark:text-gray-100 font-bold mb-4">Plans</h2>
          <div class="text-sm">This workspace’s Basic Plan is set to <strong class="font-medium">$34</strong> per month and will renew on <strong class="font-medium">July 9, 2024</strong>.</div>
        </div>

        <!-- Pricing -->
        <div>
          <!-- Toggle switch -->
          <div class="flex items-center space-x-3 mb-6">
            <div class="text-sm text-gray-500 font-medium">Monthly</div>
            <div class="form-switch">
              <input type="checkbox" id="toggle" class="sr-only" v-model="annual" />
              <label for="toggle">
                <span class="bg-white shadow-xs" aria-hidden="true"></span>
                <span class="sr-only">Pay annually</span>
              </label>
            </div>
            <div class="text-sm text-gray-500 font-medium">Annually <span class="text-green-500">(-20%)</span></div>
          </div>
          <!-- Pricing tabs -->
          <div class="grid grid-cols-12 gap-6">
            <!-- Tab 1 -->
            <div class="relative col-span-full xl:col-span-4 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700/60 shadow-xs rounded-b-lg">
              <div class="absolute top-0 left-0 right-0 h-0.5 bg-green-500" aria-hidden="true"></div>
              <div class="px-5 pt-5 pb-6 border-b border-gray-200 dark:border-gray-700/60">
                <header class="flex items-center mb-2">
                  <div class="w-6 h-6 rounded-full shrink-0 bg-green-500 mr-3">
                    <svg class="w-6 h-6 fill-current text-white" viewBox="0 0 24 24">
                      <path d="M12 17a.833.833 0 01-.833-.833 3.333 3.333 0 00-3.334-3.334.833.833 0 110-1.666 3.333 3.333 0 003.334-3.334.833.833 0 111.666 0 3.333 3.333 0 003.334 3.334.833.833 0 110 1.666 3.333 3.333 0 00-3.334 3.334c0 .46-.373.833-.833.833z" />
                    </svg>
                  </div>
                  <h3 class="text-lg text-gray-800 dark:text-gray-100 font-semibold">Basic</h3>
                </header>
                <div class="text-sm mb-2">Ideal for individuals that need a custom solution with custom tools.</div>
                <!-- Price -->
                <div class="text-gray-800 dark:text-gray-100 font-bold mb-4">
                  <span class="text-2xl">$</span><span class="text-3xl">{{ annual ? '14' : '19'}}</span><span class="text-gray-500 font-medium text-sm">/mo</span>
                </div>
                <!-- CTA -->
                <button class="btn border-gray-200 dark:border-gray-700/60 hover:border-gray-300 dark:hover:border-gray-600 text-gray-800 dark:text-gray-300 w-full">Downgrade</button>
              </div>
              <div class="px-5 pt-4 pb-5">
                <div class="text-xs text-gray-800 dark:text-gray-100 font-semibold uppercase mb-4">What's included</div>
                <!-- List -->
                <ul>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Lorem ipsum dolor sit amet</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Quis nostrud exercitation</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Lorem ipsum dolor sit amet</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Quis nostrud exercitation</div>
                  </li>
                </ul>
              </div>
            </div>
            <!-- Tab 2 -->
            <div class="relative col-span-full xl:col-span-4 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700/60 shadow-xs rounded-b-lg">
              <div class="absolute top-0 left-0 right-0 h-0.5 bg-sky-500" aria-hidden="true"></div>
              <div class="px-5 pt-5 pb-6 border-b border-gray-200 dark:border-gray-700/60">
                <header class="flex items-center mb-2">
                  <div class="w-6 h-6 rounded-full shrink-0 bg-sky-500 mr-3">
                    <svg class="w-6 h-6 fill-current text-white" viewBox="0 0 24 24">
                      <path d="M12 17a.833.833 0 01-.833-.833 3.333 3.333 0 00-3.334-3.334.833.833 0 110-1.666 3.333 3.333 0 003.334-3.334.833.833 0 111.666 0 3.333 3.333 0 003.334 3.334.833.833 0 110 1.666 3.333 3.333 0 00-3.334 3.334c0 .46-.373.833-.833.833z" />
                    </svg>
                  </div>
                  <h3 class="text-lg text-gray-800 dark:text-gray-100 font-semibold">Standard</h3>
                </header>
                <div class="text-sm mb-2">Ideal for individuals that need a custom solution with custom tools.</div>
                <!-- Price -->
                <div class="text-gray-800 dark:text-gray-100 font-bold mb-4">
                  <span class="text-2xl">$</span><span class="text-3xl">{{ annual ? '34' : '39'}}</span><span class="text-gray-500 font-medium text-sm">/mo</span>
                </div>
                <!-- CTA -->
                <button class="btn w-full bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white disabled:border-gray-200 dark:disabled:border-gray-700 disabled:bg-white dark:disabled:bg-gray-800 disabled:text-gray-300 dark:disabled:text-gray-600 disabled:cursor-not-allowed" disabled>
                  <svg class="w-3 h-3 shrink-0 fill-current mr-2" viewBox="0 0 12 12">
                    <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                  </svg>
                  <span>Current Plan</span>
                </button>
              </div>
              <div class="px-5 pt-4 pb-5">
                <div class="text-xs text-gray-800 dark:text-gray-100 font-semibold uppercase mb-4">What's included</div>
                <!-- List -->
                <ul>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Lorem ipsum dolor sit amet</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Quis nostrud exercitation</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Lorem ipsum dolor sit amet</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Quis nostrud exercitation</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Lorem ipsum dolor sit amet</div>
                  </li>
                </ul>
              </div>
            </div>
            <!-- Tab 3 -->
            <div class="relative col-span-full xl:col-span-4 bg-white dark:bg-gray-800 border border-gray-200 dark:border-gray-700/60 shadow-xs rounded-b-lg">
              <div class="absolute top-0 left-0 right-0 h-0.5 bg-violet-500" aria-hidden="true"></div>
              <div class="px-5 pt-5 pb-6 border-b border-gray-200 dark:border-gray-700/60">
                <header class="flex items-center mb-2">
                  <div class="w-6 h-6 rounded-full shrink-0 bg-violet-500 mr-3">
                    <svg class="w-6 h-6 fill-current text-white" viewBox="0 0 24 24">
                      <path d="M12 17a.833.833 0 01-.833-.833 3.333 3.333 0 00-3.334-3.334.833.833 0 110-1.666 3.333 3.333 0 003.334-3.334.833.833 0 111.666 0 3.333 3.333 0 003.334 3.334.833.833 0 110 1.666 3.333 3.333 0 00-3.334 3.334c0 .46-.373.833-.833.833z" />
                    </svg>
                  </div>
                  <h3 class="text-lg text-gray-800 dark:text-gray-100 font-semibold">Plus</h3>
                </header>
                <div class="text-sm mb-2">Ideal for individuals that need a custom solution with custom tools.</div>
                <!-- Price -->
                <div class="text-gray-800 dark:text-gray-100 font-bold mb-4">
                  <span class="text-2xl">$</span><span class="text-3xl">{{ annual ? '74' : '79'}}</span><span class="text-gray-500 font-medium text-sm">/mo</span>
                </div>
                <!-- CTA -->
                <button class="btn bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white w-full">Upgrade</button>
              </div>
              <div class="px-5 pt-4 pb-5">
                <div class="text-xs text-gray-800 dark:text-gray-100 font-semibold uppercase mb-4">What's included</div>
                <!-- List -->
                <ul>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Lorem ipsum dolor sit amet</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Quis nostrud exercitation</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Lorem ipsum dolor sit amet</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Quis nostrud exercitation</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Lorem ipsum dolor sit amet</div>
                  </li>
                  <li class="flex items-center py-1">
                    <svg class="w-3 h-3 shrink-0 fill-current text-green-500 mr-2" viewBox="0 0 12 12">
                      <path d="M10.28 1.28L3.989 7.575 1.695 5.28A1 1 0 00.28 6.695l3 3a1 1 0 001.414 0l7-7A1 1 0 0010.28 1.28z" />
                    </svg>
                    <div class="text-sm">Quis nostrud exercitation</div>
                  </li>
                </ul>
              </div>
            </div>
          </div>
        </div>
      </section>

      <!-- Contact Sales -->
      <section>
        <div class="px-5 py-3 bg-linear-to-r from-violet-500/[0.12] dark:from-violet-500/[0.24] to-violet-500/[0.04] rounded-lg text-center xl:text-left xl:flex xl:flex-wrap xl:justify-between xl:items-center">
          <div class="text-gray-800 dark:text-gray-100 font-semibold mb-2 xl:mb-0">Looking for different configurations?</div>
          <button class="btn bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white">Contact Sales</button>
        </div>
      </section>

      <!-- FAQs -->
      <section>
        <div class="my-8">
          <h2 class="text-2xl text-gray-800 dark:text-gray-100 font-bold">FAQs</h2>
        </div>
        <ul class="space-y-5">
          <li>
            <div class="font-semibold text-gray-800 dark:text-gray-100 mb-1">
              What is the difference between the three versions?
            </div>
            <div class="text-sm">
              Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit.
            </div>
          </li>
          <li>
            <div class="font-semibold text-gray-800 dark:text-gray-100 mb-1">
              Is there any difference between Basic and Plus licenses?
            </div>
            <div class="text-sm">
              Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum in voluptate velit esse cillum dolore eu fugiat nulla pariatur.
            </div>
          </li>
          <li>
            <div class="font-semibold text-gray-800 dark:text-gray-100 mb-1">
              Got more questions?
            </div>
            <div class="text-sm">
              Excepteur sint occaecat cupidatat non proident, sunt in culpa qui officia deserunt mollit anim id est laborum in voluptate velit esse cillum dolore eu fugiat <a class="font-medium text-violet-500 hover:text-violet-600 dark:hover:text-violet-400" href="#0">contact us</a>.
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
  name: 'PlansPanel',
  setup() {

    const annual = ref(true)

    return {
      annual
    }
  }  
}
</script>