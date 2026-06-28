<template>
  <main class="bg-white dark:bg-gray-900">
    <div class="relative flex">
      <!-- Content -->
      <div class="w-full md:w-1/2">
        <div class="min-h-dvh h-full flex flex-col">
          <div class="flex items-center h-16 px-4 sm:px-6 lg:px-8">
            <router-link class="block" to="/">
              <svg width="32" height="32" viewBox="0 0 32 32" aria-hidden="true">
                <defs>
                  <linearGradient x1="28.538%" y1="20.229%" x2="100%" y2="108.156%" id="auth-layout-logo-a">
                    <stop stop-color="#B7ACFF" stop-opacity="0" offset="0%" />
                    <stop stop-color="#B7ACFF" offset="100%" />
                  </linearGradient>
                  <linearGradient x1="88.638%" y1="29.267%" x2="22.42%" y2="100%" id="auth-layout-logo-b">
                    <stop stop-color="#7BC8FF" stop-opacity="0" offset="0%" />
                    <stop stop-color="#7BC8FF" offset="100%" />
                  </linearGradient>
                </defs>
                <rect fill="#8470FF" width="32" height="32" rx="16" />
                <path
                  d="M18.277.16C26.035 1.267 32 7.938 32 16c0 8.837-7.163 16-16 16a15.937 15.937 0 01-10.426-3.863L18.277.161z"
                  fill="#755FF8"
                />
                <path
                  d="M7.404 2.503l18.339 26.19A15.93 15.93 0 0116 32C7.163 32 0 24.837 0 16 0 10.327 2.952 5.344 7.404 2.503z"
                  fill="url(#auth-layout-logo-a)"
                />
                <path
                  d="M2.223 24.14L29.777 7.86A15.926 15.926 0 0132 16c0 8.837-7.163 16-16 16-5.864 0-10.991-3.154-13.777-7.86z"
                  fill="url(#auth-layout-logo-b)"
                />
              </svg>
            </router-link>
          </div>

          <div class="w-full max-w-md mx-auto px-4 py-8 sm:px-0 sm:py-12">
            <router-view />
          </div>
        </div>
      </div>

      <!-- Image -->
      <div class="hidden md:block absolute top-0 bottom-0 right-0 md:w-1/2" aria-hidden="true">
        <img
          class="object-cover object-center w-full h-full"
          src="@/images/auth-image.jpg"
          width="760"
          height="1024"
          alt="Authentication"
        />
      </div>
    </div>
  </main>
</template>

<script setup lang="ts">
</script>