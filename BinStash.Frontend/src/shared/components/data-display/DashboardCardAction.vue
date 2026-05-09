<template>
  <router-link
    :to="to"
    class="group flex items-center gap-3 rounded-[20px] border border-transparent px-3 py-2.5 transition-all hover:border-[#3B4285] hover:bg-[#19284F] dark:hover:border-[#3B4285] dark:hover:bg-[#19284F]"
  >
    <div
      class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full transition-all group-hover:text-white group-hover:shadow-sm"
      :class="[iconBgClass, iconColorClass, iconHoverBgClass]"
    >
      <component :is="icon" class="h-4 w-4" />
    </div>
    <div>
      <div class="text-sm font-semibold text-slate-900 transition-colors group-hover:text-white dark:text-white">
        {{ title }}
      </div>
      <div class="text-xs text-slate-500 transition-colors group-hover:text-slate-400 dark:text-slate-400">
        {{ subtitle }}
      </div>
    </div>
  </router-link>
</template>

<script>
export default {
  name: 'DashboardCardAction',
  props: {
    to: {
      type: [String, Object],
      required: true,
    },
    icon: {
      type: [Object, Function],
      required: true,
    },
    title: {
      type: String,
      required: true,
    },
    subtitle: {
      type: String,
      default: '',
    },
    iconBgClass: {
      type: String,
      default: 'bg-[#7C86FF]/15',
    },
    iconColorClass: {
      type: String,
      default: 'text-[#7C86FF]',
    },
    iconHoverBgClass: {
      type: String,
      default: 'group-hover:bg-[#7C86FF]',
    },
  },
}
</script>