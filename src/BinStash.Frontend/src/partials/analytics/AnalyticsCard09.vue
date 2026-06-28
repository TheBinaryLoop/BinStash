<template>
  <div class="flex flex-col col-span-full sm:col-span-6 xl:col-span-4 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60">
      <h2 class="font-semibold text-gray-800 dark:text-gray-100">Sessions By Age</h2>
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
  name: 'AnalyticsCard09',
  components: {
    DoughnutChart,
  },
  setup() {
    const chartData = ref({
      labels: ['<18', '18-24', '24-36', '>35'],
      datasets: [
        {
          label: 'Visit By Age Category',
          data: [
            30, 50, 5, 15,
          ],
          backgroundColor: [
            getCssVariable('--color-violet-500'),
            getCssVariable('--color-sky-500'),
            getCssVariable('--color-rose-500'),
            getCssVariable('--color-green-500'),
          ],
          hoverBackgroundColor: [
            getCssVariable('--color-violet-600'),
            getCssVariable('--color-sky-600'),
            getCssVariable('--color-rose-600'),
            getCssVariable('--color-green-600'),
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