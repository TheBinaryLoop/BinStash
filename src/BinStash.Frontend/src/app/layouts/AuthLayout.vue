<template>
  <main class="min-h-dvh bg-canvas text-ink-strong">
    <div class="flex min-h-dvh">
      <!-- Form column -->
      <div class="flex w-full flex-col md:w-1/2">
        <div class="flex h-16 items-center px-4 sm:px-6 lg:px-8">
          <router-link class="flex items-center gap-2" to="/">
            <span class="flex size-8 items-center justify-center rounded-[10px] bg-linear-to-br from-brand-from to-brand-to">
              <span class="size-5 rounded-[4px] border-2 border-white" />
            </span>
            <span class="text-sm font-semibold tracking-tight text-ink-strong">BinStash</span>
          </router-link>
        </div>

        <div class="flex flex-1 items-center">
          <div class="mx-auto w-full max-w-md px-4 py-8 sm:px-8 sm:py-12">
            <router-view />
          </div>
        </div>
      </div>

      <!-- Brand hero -->
      <div class="relative hidden w-1/2 overflow-hidden bg-linear-to-br from-brand-from to-brand-to md:block" aria-hidden="true">
        <div class="absolute -left-24 -top-24 size-96 rounded-full bg-white/10 blur-3xl" />
        <div class="absolute -bottom-32 -right-16 size-96 rounded-full bg-black/20 blur-3xl" />
        <div class="relative flex h-full flex-col items-center justify-center gap-6 px-12 text-center text-white">
          <span class="flex size-20 items-center justify-center rounded-[24px] border border-white/20 bg-white/10 backdrop-blur-sm">
            <span class="size-10 rounded-[8px] border-2 border-white" />
          </span>
          <h2 class="text-3xl font-bold tracking-tight">BinStash</h2>
          <p class="max-w-sm text-sm leading-relaxed text-white/80">
            Content-defined, deduplicated, compressed storage for your build artifacts and release packages.
          </p>
        </div>
      </div>
    </div>
  </main>
</template>

<script setup lang="ts">
</script>