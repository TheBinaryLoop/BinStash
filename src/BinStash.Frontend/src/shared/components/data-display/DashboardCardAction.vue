<template>
  <router-link
    :to="to"
    class="group flex items-center gap-3 rounded-[20px] border border-transparent px-3 py-2.5 transition-all hover:border-hairline hover:bg-raised"
  >
    <div
      class="flex h-9 w-9 shrink-0 items-center justify-center rounded-full transition-all group-hover:text-white group-hover:shadow-sm"
      :class="[iconBgClass, iconColorClass, iconHoverBgClass]"
    >
      <component :is="icon" class="h-4 w-4" />
    </div>
    <div>
      <div class="text-sm font-semibold text-ink-strong transition-colors group-hover:text-ink-strong">
        {{ title }}
      </div>
      <div class="text-xs text-ink-muted transition-colors">
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
      default: 'bg-accent-soft',
    },
    iconColorClass: {
      type: String,
      default: 'text-accent',
    },
    iconHoverBgClass: {
      type: String,
      default: 'group-hover:bg-accent',
    },
  },
}
</script>