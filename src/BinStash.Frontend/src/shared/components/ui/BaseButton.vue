<template>
  <component
    :is="tag"
    :to="to"
    :href="href"
    :type="tag === 'button' ? type : undefined"
    :disabled="tag === 'button' ? (disabled || loading) : undefined"
    :aria-busy="loading || undefined"
    :class="classes"
  >
    <Spinner
      v-if="loading"
      :size="size === 'lg' ? 18 : 16"
      :thickness="2"
      color="currentColor"
      class="shrink-0"
    />
    <component :is="icon" v-else-if="icon" :class="iconSize" class="shrink-0" />
    <span v-if="!iconOnly"><slot /></span>
    <component :is="trailingIcon" v-if="trailingIcon && !loading" :class="iconSize" class="shrink-0" />
  </component>
</template>

<script setup lang="ts">
import { computed, type Component } from 'vue'
import { RouterLink } from 'vue-router'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const props = withDefaults(defineProps<{
  variant?: 'primary' | 'secondary' | 'ghost' | 'subtle' | 'danger'
  size?: 'sm' | 'md' | 'lg'
  type?: 'button' | 'submit' | 'reset'
  to?: string | object
  href?: string
  loading?: boolean
  disabled?: boolean
  block?: boolean
  icon?: Component
  trailingIcon?: Component
  iconOnly?: boolean
}>(), {
  variant: 'primary',
  size: 'md',
  type: 'button',
  loading: false,
  disabled: false,
  block: false,
  iconOnly: false,
})

const tag = computed(() => (props.to ? RouterLink : props.href ? 'a' : 'button'))

const iconSize = computed(() => (props.size === 'sm' ? 'h-4 w-4' : props.size === 'lg' ? 'h-5 w-5' : 'h-4 w-4'))

const variantClasses: Record<string, string> = {
  primary: 'bg-accent text-white hover:bg-accent-hover shadow-sm',
  secondary: 'bg-card text-ink-strong border border-hairline hover:bg-raised',
  ghost: 'text-ink-muted hover:bg-raised hover:text-ink-strong',
  subtle: 'bg-accent-soft text-accent hover:brightness-110',
  danger: 'bg-danger text-white hover:brightness-110 shadow-sm',
}

const sizeClasses = computed(() => {
  if (props.iconOnly) {
    return props.size === 'sm' ? 'h-8 w-8' : props.size === 'lg' ? 'h-11 w-11' : 'h-9 w-9'
  }
  return props.size === 'sm'
    ? 'h-8 px-3 text-xs gap-1.5'
    : props.size === 'lg'
      ? 'h-11 px-5 text-sm gap-2'
      : 'h-9 px-3.5 text-sm gap-2'
})

const classes = computed(() => [
  'inline-flex items-center justify-center rounded-control font-medium transition select-none whitespace-nowrap',
  'focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-accent/50 focus-visible:ring-offset-2 focus-visible:ring-offset-canvas',
  'disabled:opacity-50 disabled:pointer-events-none',
  props.block ? 'w-full' : '',
  sizeClasses.value,
  variantClasses[props.variant],
  props.disabled && tag.value !== 'button' ? 'opacity-50 pointer-events-none' : '',
])
</script>
