<template>
  <div class="flex flex-col col-span-full sm:col-span-6 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60">
        <h2 class="font-semibold text-gray-800 dark:text-gray-100">Audience Overview</h2>
    </header>
    <!-- Chart built with Chart.js 3 -->
    <!-- Change the height attribute to adjust the chart height -->
    <BarChart :data="chartData" width="595" height="248" />
  </div>  
</template>

<script>
import { ref } from 'vue'
import BarChart from '../../charts/BarChart04.vue'

// Import utilities
import { getCssVariable } from '../../utils/Utils'

export default {
  name: 'AnalyticsCard04',
  components: {
    BarChart,
  },
  setup() {
    const chartData = ref({
      labels: [
        '02-01-2023', '03-01-2023', '04-01-2023', '05-01-2023',
      ],
      datasets: [
        // Blue bars
        {
          label: 'New Visitors',
          data: [
            8000, 3800, 5350, 7800,
          ],
          backgroundColor: getCssVariable('--color-violet-500'),
          hoverBackgroundColor: getCssVariable('--color-violet-600'),
          categoryPercentage: 0.7,
          borderRadius: 4,
        },
        // Light blue bars
        {
          label: 'Returning Visitors',
          data: [
            4000, 6500, 2200, 5800,
          ],
          backgroundColor: getCssVariable('--color-sky-500'),
          hoverBackgroundColor: getCssVariable('--color-sky-600'),
          categoryPercentage: 0.7,
          borderRadius: 4,
        },
      ],
    })

    return {
      chartData,
    } 
  }
}
</script>