<template>
  <div class="flex flex-col col-span-full sm:col-span-6 xl:col-span-4 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60">
      <h2 class="font-semibold text-gray-800 dark:text-gray-100">Sessions By Gender</h2>
    </header>
    <!-- Chart built with Chart.js 3 -->
    <!-- Change the height attribute to adjust the chart height -->
    <PolarChart :data="chartData" width="389" height="260" />
  </div>
</template>

<script>
import { ref } from 'vue'
import PolarChart from '../../charts/PolarChart.vue'

// Import utilities
import { getCssVariable } from '../../utils/Utils'

// Import utilities
import { adjustColorOpacity } from '../../utils/Utils'

export default {
  name: 'AnalyticsCard10',
  components: {
    PolarChart,
  },
  setup() {
    const chartData = ref({
      labels: ['Males', 'Females', 'Unknown'],
      datasets: [
        {
          label: 'Sessions By Gender',
          data: [
            500, 326, 242,
          ],
          backgroundColor: [
            adjustColorOpacity(getCssVariable('--color-violet-500'), 0.8),
            adjustColorOpacity(getCssVariable('--color-sky-500'), 0.8),
            adjustColorOpacity(getCssVariable('--color-green-500'), 0.8),
          ],
          hoverBackgroundColor: [
            adjustColorOpacity(getCssVariable('--color-violet-600'), 0.8),
            adjustColorOpacity(getCssVariable('--color-sky-600'), 0.8),
            adjustColorOpacity(getCssVariable('--color-green-600'), 0.8),
          ],
          borderWidth: 0,
        },
      ],
    })

    return {
      chartData,
    } 
  }
}
</script>