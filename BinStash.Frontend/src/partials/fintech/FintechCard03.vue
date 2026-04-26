<template>
  <div class="flex flex-col col-span-full sm:col-span-6 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60">
      <h2 class="font-semibold text-gray-800 dark:text-gray-100">Cash Flow</h2>
    </header>
    <!-- Chart built with Chart.js 3 -->
    <!-- Change the height attribute to adjust the chart height -->
    <BarChart :data="chartData" width="595" height="248" />
  </div>
</template>

<script>
import { ref } from 'vue'
import BarChart from '../../charts/BarChart05.vue'

// Import utilities
import { getCssVariable } from '../../utils/Utils'

export default {
  name: 'FintechCard03',
  components: {
    BarChart,
  },
  setup() {
    const chartData = ref({
      labels: [
        '12-01-2022', '01-01-2023', '02-01-2023',
        '03-01-2023', '04-01-2023', '05-01-2023'
      ],
      datasets: [
        // Indigo bars
        {
          label: 'Inflow',
          data: [800, 2600, 4000, 1200, 3200, 1700],
          backgroundColor: getCssVariable('--color-violet-500'),
          hoverBackgroundColor: getCssVariable('--color-violet-600'),
          barPercentage: 0.7,
          categoryPercentage: 0.7,
          borderRadius: 4,
        },
        // Grey bars
        {
          label: 'Outflow',
          data: [2800, 1700, 900, 2900, 1950, 3100],
          backgroundColor: getCssVariable('--color-violet-200'),
          hoverBackgroundColor: getCssVariable('--color-violet-300'),
          barPercentage: 0.7,
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