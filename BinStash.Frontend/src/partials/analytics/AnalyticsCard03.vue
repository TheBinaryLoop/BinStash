<template>
  <div class="flex flex-col col-span-full sm:col-span-6 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60 flex items-center">
      <h2 class="font-semibold text-gray-800 dark:text-gray-100">Acquisition Channels</h2>
    </header>
    <!-- Chart built with Chart.js 3 -->
    <!-- Change the height attribute to adjust the chart height -->
    <BarChart :data="chartData" width="595" height="248" />
  </div>  
</template>

<script>
import { ref } from 'vue'
import BarChart from '../../charts/BarChart03.vue'

// Import utilities
import { getCssVariable } from '../../utils/Utils'

export default {
  name: 'AnalyticsCard03',
  components: {
    BarChart,
  },
  setup() {
    const chartData = ref({
      labels: [
        '12-01-2022', '01-01-2023', '02-01-2023',
        '03-01-2023', '04-01-2023', '05-01-2023',
      ],
      datasets: [
        // Stack
        {
          label: 'Direct',
          data: [
            5000, 4000, 4000, 3800, 5200, 5100,
          ],
          backgroundColor: getCssVariable('--color-violet-700'),
          hoverBackgroundColor: getCssVariable('--color-violet-800'),
          barPercentage: 0.7,
          categoryPercentage: 0.7,
          borderRadius: 4,
        },
        // Stack
        {
          label: 'Referral',
          data: [
            2500, 2600, 4000, 4000, 4800, 3500,
          ],
          backgroundColor: getCssVariable('--color-violet-500'),
          hoverBackgroundColor: getCssVariable('--color-violet-600'),
          barPercentage: 0.7,
          categoryPercentage: 0.7,
          borderRadius: 4,
        },
        // Stack
        {
          label: 'Organic Search',
          data: [
            2300, 2000, 3100, 2700, 1300, 2600,
          ],
          backgroundColor: getCssVariable('--color-violet-300'),
          hoverBackgroundColor: getCssVariable('--color-violet-400'),
          barPercentage: 0.7,
          categoryPercentage: 0.7,
          borderRadius: 4,
        },
        // Stack
        {
          label: 'Social',
          data: [
            4800, 4200, 4800, 1800, 3300, 3500,
          ],
          backgroundColor: getCssVariable('--color-violet-100'),
          hoverBackgroundColor: getCssVariable('--color-violet-200'),
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