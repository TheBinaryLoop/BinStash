<template>
  <main class="bg-white dark:bg-gray-900">

    <div class="relative flex">

      <!-- Content -->
      <div class="w-full md:w-1/2">

        <div class="min-h-[100dvh] h-full flex flex-col after:flex-1">

          <div class="flex-1">

            <!-- Header -->
            <div class="flex items-center justify-between h-16 px-4 sm:px-6 lg:px-8">
              <!-- Logo -->
              <router-link class="block" to="/">
                <svg class="fill-violet-500" xmlns="http://www.w3.org/2000/svg" width="32" height="32">
                  <path d="M31.956 14.8C31.372 6.92 25.08.628 17.2.044V5.76a9.04 9.04 0 0 0 9.04 9.04h5.716ZM14.8 26.24v5.716C6.92 31.372.63 25.08.044 17.2H5.76a9.04 9.04 0 0 1 9.04 9.04Zm11.44-9.04h5.716c-.584 7.88-6.876 14.172-14.756 14.756V26.24a9.04 9.04 0 0 1 9.04-9.04ZM.044 14.8C.63 6.92 6.92.628 14.8.044V5.76a9.04 9.04 0 0 1-9.04 9.04H.044Z" />
                </svg>   
              </router-link>
              <div class="text-sm">
                Have an account? <router-link class="font-medium text-violet-500 hover:text-violet-600 dark:hover:text-violet-400" to="/signin">Sign In</router-link>
              </div>
            </div>

            <!-- Progress bar -->
            <div class="px-4 pt-12 pb-8">
              <div class="max-w-md mx-auto w-full">
                <div class="relative">
                  <div class="absolute left-0 top-1/2 -mt-px w-full h-0.5 bg-gray-200 dark:bg-gray-700/60" aria-hidden="true"></div>
                  <ul class="relative flex justify-between w-full">
                    <li>
                      <router-link class="flex items-center justify-center w-6 h-6 rounded-full text-xs font-semibold bg-violet-500 text-white" to="/onboarding-01">1</router-link>
                    </li>
                    <li>
                      <router-link class="flex items-center justify-center w-6 h-6 rounded-full text-xs font-semibold bg-violet-500 text-white" to="/onboarding-02">2</router-link>
                    </li>
                    <li>
                      <router-link class="flex items-center justify-center w-6 h-6 rounded-full text-xs font-semibold bg-violet-500 text-white" to="/onboarding-03">3</router-link>
                    </li>
                    <li>
                      <router-link class="flex items-center justify-center w-6 h-6 rounded-full text-xs font-semibold bg-white dark:bg-gray-900 text-gray-500 dark:text-gray-400" to="/onboarding-04">4</router-link>
                    </li>
                  </ul>
                </div>
              </div>
            </div>
          </div>

          <div class="px-4 py-8">
            <div class="max-w-md mx-auto">

              <h1 class="text-3xl text-gray-800 dark:text-gray-100 font-bold mb-6">Company information</h1>
              <!-- Form -->
              <form>
                <div class="space-y-4 mb-8">
                  <!-- Company Name -->
                  <div>
                    <label class="block text-sm font-medium mb-1" for="company-name">Company Name <span class="text-red-500">*</span></label>
                    <input id="company-name" class="form-input w-full" type="text" />
                  </div>
                  <!-- City and Postal Code -->
                  <div class="flex space-x-4">
                    <div class="flex-1">
                      <label class="block text-sm font-medium mb-1" for="city">City <span class="text-red-500">*</span></label>
                      <input id="city" class="form-input w-full" type="text" />
                    </div>
                    <div class="flex-1">
                      <label class="block text-sm font-medium mb-1" for="postal-code">Postal Code <span class="text-red-500">*</span></label>
                      <input id="postal-code" class="form-input w-full" type="text" />
                    </div>
                  </div>
                  <!-- Street Address -->
                  <div>
                    <label class="block text-sm font-medium mb-1" for="street">Street Address <span class="text-red-500">*</span></label>
                    <input id="street" class="form-input w-full" type="text" />
                  </div>
                  <!-- Country -->
                  <div>
                    <label class="block text-sm font-medium mb-1" for="country">Country <span class="text-red-500">*</span></label>
                    <select id="country" class="form-select w-full">
                      <option>USA</option>
                      <option>Italy</option>
                      <option>United Kingdom</option>
                    </select>
                  </div>
                </div>
                <div class="flex items-center justify-between">
                  <router-link class="text-sm underline hover:no-underline" to="/onboarding-02">&lt;- Back</router-link>
                  <router-link class="btn bg-gray-900 text-gray-100 hover:bg-gray-800 dark:bg-gray-100 dark:text-gray-800 dark:hover:bg-white ml-auto" to="/onboarding-04">Next Step -&gt;</router-link>
                </div>
              </form>

            </div>
          </div>

        </div>

      </div>

      <!-- Image -->
      <div class="hidden md:block absolute top-0 bottom-0 right-0 md:w-1/2" aria-hidden="true">
        <img class="object-cover object-center w-full h-full" src="../images/onboarding-image.jpg" width="760" height="1024" alt="Onboarding" />
      </div>

    </div>

  </main>
</template>

<script>

export default {
  name: 'Onboarding03',
}
</script>