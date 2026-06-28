<template>
  <div class="rounded-card border border-hairline bg-card shadow-sm">
    <div class="flex items-center justify-between border-b border-hairline px-5 py-4">
      <div class="flex items-center gap-3">
        <div
          class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full"
          :class="[iconBgClass, iconColorClass]"
        >
          <component :is="icon" class="h-4 w-4" />
        </div>
        <div>
          <h3 class="font-semibold text-ink-strong">{{ title }}</h3>
          <p v-if="subtitle" class="text-xs text-ink-subtle">{{ subtitle }}</p>
        </div>
      </div>
      <slot name="header-action" />
    </div>
    <slot />
  </div>
</template>

<script>
export default {
  name: 'DashboardCard',
  props: {
    title: {
      type: String,
      required: true,
    },
    subtitle: {
      type: String,
      default: '',
    },
    icon: {
      type: [Object, Function],
      required: true,
    },
    iconBgClass: {
      type: String,
      default: 'bg-accent-soft',
    },
    iconColorClass: {
      type: String,
      default: 'text-accent',
    },
  },
}
</script>