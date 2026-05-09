<template>
  <svg
    xmlns="http://www.w3.org/2000/svg"
    :width="width"
    :height="height"
    viewBox="0 0 100 32"
    preserveAspectRatio="none"
    class="overflow-visible"
  >
    <defs>
      <linearGradient :id="gradientId" x1="0" y1="0" x2="0" y2="1">
        <stop offset="0%" :stop-color="color" stop-opacity="0.25" />
        <stop offset="100%" :stop-color="color" stop-opacity="0" />
      </linearGradient>
    </defs>
    <polygon v-if="areaPoints" :points="areaPoints" :fill="`url(#${gradientId})`" />
    <polyline
      v-if="linePoints"
      :points="linePoints"
      fill="none"
      :stroke="color"
      stroke-width="2.5"
      stroke-linejoin="round"
      stroke-linecap="round"
    />
  </svg>
</template>

<script>
import { computed } from 'vue'

let uid = 0

export default {
  name: 'SparklineChart',
  props: {
    data: {
      type: Array,
      required: true,
    },
    color: {
      type: String,
      default: '#8b5cf6',
    },
    width: {
      type: [String, Number],
      default: '100%',
    },
    height: {
      type: [String, Number],
      default: 40,
    },
  },
  setup(props) {
    const gradientId = `sg-${++uid}`

    const linePoints = computed(() => {
      const d = props.data
      if (!d || d.length < 2) return ''
      const min = Math.min(...d)
      const max = Math.max(...d)
      const range = max - min || 1
      const step = 100 / (d.length - 1)
      return d
        .map((v, i) => {
          const x = i * step
          const y = 29 - ((v - min) / range) * 26
          return `${x.toFixed(2)},${y.toFixed(2)}`
        })
        .join(' ')
    })

    const areaPoints = computed(() => {
      if (!linePoints.value) return ''
      const pts = linePoints.value.split(' ')
      const firstX = pts[0].split(',')[0]
      const lastX = pts[pts.length - 1].split(',')[0]
      return `${firstX},31 ${linePoints.value} ${lastX},31`
    })

    return { gradientId, linePoints, areaPoints }
  },
}
</script>