<template>
  <div>
    <AuthPageHeader title="Reset your Password" />

    <!-- Form -->
    <AuthCard :padded="true">
      <form>
        <div class="space-y-4">
          <div>
            <label class="block text-sm font-medium mb-1" for="email">Email Address <span class="text-red-500">*</span></label>
            <input id="email" class="form-input w-full" type="email" />
          </div>
        </div>
        <div class="flex justify-end mt-6">
          <button class="btn bg-violet-500 text-white hover:bg-violet-600 dark:bg-violet-500 dark:hover:bg-violet-600 whitespace-nowrap ml-3">Send Reset Link</button>
        </div>
      </form>
    </AuthCard>
  </div>
</template>

<script setup lang="ts">
import AuthCard from '@/shared/components/auth/AuthCard.vue'
import AuthPageHeader from '@/shared/components/auth/AuthPageHeader.vue'

</script>