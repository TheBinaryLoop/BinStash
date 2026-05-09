<template>
  <div class="flex flex-col col-span-full sm:col-span-6 xl:col-span-4 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60 flex items-center">
      <h2 class="font-semibold text-gray-800 dark:text-gray-100">Growth Portfolio</h2>
    </header>
    <div class="px-5 py-3">
      <div class="text-sm italic mb-2">Hey Mark, by age 65 you could have:</div>
      <div class="text-3xl font-bold text-gray-800 dark:text-gray-100">$2M - $3.5M</div>
      <div class="text-sm text-gray-500 dark:text-gray-400">Risk level 8</div>
    </div>
    <!-- Chart built with Chart.js 3 -->
    <div class="grow">
      <!-- Change the height attribute to adjust the chart height -->
      <LineChart :data="chartData" width="389" height="262" />
    </div>
  </div>
</template>

<script>
import { ref } from 'vue'
import { chartAreaGradient } from '../../charts/ChartjsConfig'
import LineChart from '../../charts/LineChart07.vue'

// Import utilities
import { adjustColorOpacity, getCssVariable } from '../../utils/Utils'

export default {
  name: 'FintechCard08',
  components: {
    LineChart,
  },
  setup() {
    const chartData = ref({
      labels: ['2010', 'Age 65'],
      datasets: [
        // Dark green line
        {
          label: 'Growth 1',
          data: [0, 3500000],
          borderColor: getCssVariable('--color-green-500'),
          fill: true,
          backgroundColor: function(context) {
            const chart = context.chart;
            const {ctx, chartArea} = chart;
            return chartAreaGradient(ctx, chartArea, [
              { stop: 0, color: adjustColorOpacity(getCssVariable('--color-green-500'), 0) },
              { stop: 1, color: adjustColorOpacity(getCssVariable('--color-green-500'), 0.2) }
            ]);
          },          
          borderWidth: 2,
          pointRadius: 0,
          pointHoverRadius: 3,
          pointBackgroundColor: getCssVariable('--color-green-500'),
          clip: 20,
          tension: 0.2,
        },
        // Light green line
        {
          label: 'Growth 2',
          data: [0, 2000000],
          borderColor: getCssVariable('--color-green-200'),
          fill: false,
          borderWidth: 2,
          pointRadius: 0,
          pointHoverRadius: 3,
          pointBackgroundColor: getCssVariable('--color-green-200'),
          clip: 20,
          tension: 0.2,
        },
      ],
    })

    return {
      chartData,
    } 
  }
}
</script>