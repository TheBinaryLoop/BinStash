<template>
  <div class="flex flex-col col-span-full sm:col-span-6 xl:col-span-4 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60">
      <h2 class="font-semibold text-gray-800 dark:text-gray-100">Sessions By Device</h2>
    </header>
    <!-- Chart built with Chart.js 3 -->
    <!-- Change the height attribute to adjust the chart height -->
    <DoughnutChart :data="chartData" width="389" height="260" />
  </div>
</template>

<script>
import { ref } from 'vue'
import DoughnutChart from '../../charts/DoughnutChart.vue'

// Import utilities
import { getCssVariable } from '../../utils/Utils'

export default {
  name: 'AnalyticsCard08',
  components: {
    DoughnutChart,
  },
  setup() {
    const chartData = ref({
      labels: ['Desktop', 'Mobile', 'Tablet'],
      datasets: [
        {
          label: 'Sessions By Device',
          data: [
            12, 50, 38,
          ],
          backgroundColor: [
            getCssVariable('--color-violet-500'),
            getCssVariable('--color-sky-500'),
            getCssVariable('--color-violet-800'),
          ],
          hoverBackgroundColor: [
            getCssVariable('--color-violet-600'),
            getCssVariable('--color-sky-600'),
            getCssVariable('--color-violet-900'),
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