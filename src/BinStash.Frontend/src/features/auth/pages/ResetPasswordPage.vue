<template>
  <div>
    <AuthPageHeader title="Reset your Password" />

    <!-- Form -->
    <AuthCard :padded="true">
      <form>
        <div class="space-y-4">
          <BaseInput
            label="Email Address"
            type="email"
            required
          />
        </div>
        <div class="flex justify-end mt-6">
          <BaseButton type="submit">Send Reset Link</BaseButton>
        </div>
      </form>
    </AuthCard>
  </div>
</template>

<script setup lang="ts">
import AuthCard from '@/shared/components/auth/AuthCard.vue'
import AuthPageHeader from '@/shared/components/auth/AuthPageHeader.vue'
import { BaseInput, BaseButton } from '@/shared/components/ui'

</script>