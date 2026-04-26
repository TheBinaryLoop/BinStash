<template>
  <div class="flex flex-col col-span-full sm:col-span-6 xl:col-span-3 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <div class="px-5 pt-5">
      <header>
        <h3 class="text-sm font-semibold text-gray-500 uppercase mb-1">
          <span class="text-gray-800 dark:text-gray-100">Google</span> - Alphabet
        </h3>
        <div class="text-2xl font-bold text-gray-800 dark:text-gray-100 mb-1">$2,860.96</div>
        <div class="text-sm">
          <span class="font-medium text-red-500">-$49 (4,7%)</span> - Today
        </div>
      </header>
    </div>
    <!-- Chart built with Chart.js 3 -->
    <div class="grow">
      <!-- Change the height attribute to adjust the chart height -->
      <LineChart :data="chartData" width="286" height="98" />
    </div>
  </div>
</template>

<script>
import { ref } from 'vue'
import { chartAreaGradient } from '../../charts/ChartjsConfig'
import LineChart from '../../charts/LineChart08.vue'

// Import utilities
import { getCssVariable } from '../../utils/Utils'

// Import utilities
import { adjustColorOpacity } from '../../utils/Utils'

export default {
  name: 'FintechCard10',
  components: {
    LineChart,
  },
  setup() {
    const chartData = ref({
      labels: [
        '12-01-2022', '01-01-2023', '02-01-2023',
        '03-01-2023', '04-01-2023', '05-01-2023',
        '06-01-2023', '07-01-2023', '08-01-2023',
        '09-01-2023', '10-01-2023', '11-01-2023',
        '12-01-2023', '01-01-2024', '02-01-2024',
        '03-01-2024', '04-01-2024', '05-01-2024',
        '06-01-2024', '07-01-2024', '08-01-2024',
        '09-01-2024', '10-01-2024', '11-01-2024',
        '12-01-2024', '01-01-2025',
      ],
      datasets: [
        // Line
        {
          data: [
            732, 610, 610, 504, 504, 504, 349,
            349, 504, 342, 504, 610, 391, 192,
            154, 273, 191, 191, 126, 263, 349,
            252, 323, 322, 270, 232,
          ],
          fill: true,
          backgroundColor: function(context) {
            const chart = context.chart;
            const {ctx, chartArea} = chart;
            return chartAreaGradient(ctx, chartArea, [
              { stop: 0, color: adjustColorOpacity(getCssVariable('--color-red-500'), 0) },
              { stop: 1, color: adjustColorOpacity(getCssVariable('--color-red-500'), 0.2) }
            ]);
          }, 
          borderColor: getCssVariable('--color-rose-500'),
          borderWidth: 2,
          pointRadius: 0,
          pointHoverRadius: 3,
          pointBackgroundColor: getCssVariable('--color-rose-500'),
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