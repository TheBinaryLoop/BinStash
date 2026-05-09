<template>
  <div class="rounded-[28px] border border-slate-200 bg-white shadow-sm transition-colors dark:border-white/5 dark:bg-[#0C112D]">
    <div class="flex items-center justify-between border-b border-slate-200 px-5 py-4 dark:border-white/5">
      <div class="flex items-center gap-3">
        <div
          class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full"
          :class="[iconBgClass, iconColorClass]"
        >
          <component :is="icon" class="h-4 w-4" />
        </div>
        <div>
          <h3 class="font-semibold text-slate-900 dark:text-white">{{ title }}</h3>
          <p v-if="subtitle" class="text-xs text-slate-400 dark:text-slate-500">{{ subtitle }}</p>
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
      default: 'bg-[#7C86FF]/15',
    },
    iconColorClass: {
      type: String,
      default: 'text-[#7C86FF]',
    },
  },
}
</script>