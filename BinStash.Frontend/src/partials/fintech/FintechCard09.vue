<template>
  <div class="flex flex-col col-span-full sm:col-span-6 xl:col-span-4 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header className="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60 flex items-center">
      <h2 className="font-semibold text-gray-800 dark:text-gray-100">Portfolio Value</h2>
    </header>
    <div className="px-5 py-3">
      <div className="text-sm italic mb-2">Hey Mark, here is the value of your portfolio:</div>
      <div className="text-3xl font-bold text-gray-800 dark:text-gray-100">$224,807.27</div>
    </div>
    <!-- Chart built with Chart.js 3 -->
    <!-- Change the height attribute to adjust the chart height -->
    <PieChart :data="chartData" width="389" height="220" />
  </div>
</template>

<script>
import { ref } from 'vue'
import PieChart from '../../charts/PieChart.vue'

// Import utilities
import { getCssVariable } from '../../utils/Utils'

export default {
  name: 'FintechCard09',
  components: {
    PieChart,
  },
  setup() {
    const chartData = ref({
      labels: ['Cash', 'Commodities', 'Bonds', 'Stock'],
      datasets: [
        {
          label: 'Sessions By Device',
          data: [12, 13, 10, 65],
          backgroundColor: [
            getCssVariable('--color-green-400'),
            getCssVariable('--color-yellow-400'),
            getCssVariable('--color-sky-500'),
            getCssVariable('--color-violet-500'),
          ],
          hoverBackgroundColor: [
            getCssVariable('--color-green-500'),
            getCssVariable('--color-yellow-500'),
            getCssVariable('--color-sky-600'),
            getCssVariable('--color-violet-600'),
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