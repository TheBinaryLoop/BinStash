<template>
  <div class="flex flex-col col-span-full xl:col-span-8 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60 flex items-center">
        <h2 class="font-semibold text-gray-800 dark:text-gray-100">Portfolio Returns</h2>
    </header>
    <!-- Chart built with Chart.js 3 -->
    <!-- Change the height attribute to adjust the chart height -->
    <LineChart :data="chartData" width="800" height="300" />
  </div>
</template>

<script>
import { ref } from 'vue'
import { chartAreaGradient } from '../../charts/ChartjsConfig'
import LineChart from '../../charts/LineChart05.vue'

// Import utilities
import { adjustColorOpacity, getCssVariable } from '../../utils/Utils'

export default {
  name: 'FintechCard01',
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
        '12-01-2024', '01-01-2025', '02-01-2025',
        '03-01-2025', '04-01-2025', '05-01-2025',
        '06-01-2025', '07-01-2025', '08-01-2025',
        '09-01-2025', '10-01-2025', '11-01-2025',
        '12-01-2025', '01-01-2026', '02-01-2026',
        '03-01-2026', '04-01-2026',
      ],
      datasets: [
        // Indigo line
        {
          label: 'Mosaic Portfolio',
          data: [
            0, 2.5, 2.5, 4, 2.5, 3.8, 5, 9, 7.5, 11,
            14, 15, 17, 15, 14, 9, 15, 26, 16, 18,
            15, 20, 18, 19, 19, 24, 29, 26, 39, 27,
            35, 32, 29, 35, 36, 34, 39, 36, 41, 41,
            48,
          ],
          borderColor: getCssVariable('--color-violet-500'),
          fill: true,
          backgroundColor: function(context) {
            const chart = context.chart;
            const {ctx, chartArea} = chart;
            return chartAreaGradient(ctx, chartArea, [
              { stop: 0, color: adjustColorOpacity(getCssVariable('--color-violet-500'), 0) },
              { stop: 1, color: adjustColorOpacity(getCssVariable('--color-violet-500'), 0.2) }
            ]);
          },
          borderWidth: 2,
          pointRadius: 0,
          pointHoverRadius: 3,
        pointBackgroundColor: getCssVariable('--color-violet-500'),
        pointHoverBackgroundColor: getCssVariable('--color-violet-500'),
        pointBorderWidth: 0,
        pointHoverBorderWidth: 0,          
        clip: 20,
        tension: 0.2,
        },
        // Yellow line
        {
          label: 'Expected Return',
          data: [
            0, 1, 2, 3, 4, 5, 6, 7, 8, 9,
            10, 11, 12, 13, 14, 15, 16, 17, 18, 19,
            20, 21, 22, 23, 24, 25, 26, 27, 28, 29,
            30, 31, 32, 33, 34, 35, 36, 37, 38, 39,
            40,
          ],
          borderColor: getCssVariable('--color-yellow-400'),
          borderDash: [4, 4],
          fill: false,
          borderWidth: 2,
          pointRadius: 0,
          pointHoverRadius: 3,
          pointBackgroundColor: getCssVariable('--color-yellow-400'),
          clip: 20,
          tension: 0.2,
        },
        // gray line
        {
          label: 'Competitors',
          data: [
            0.7, 3.5, 4.5, 3.5, 4.2, 4.6, 6, 7, 6, 6,
            11, 13, 14, 18, 17, 15, 13, 16, 20, 21,
            24, 22, 20, 22, 25, 18, 21, 23, 24, 32,
            28, 29, 35, 37, 42, 32, 32, 33, 33, 37,
            32,
          ],
          borderColor: adjustColorOpacity(getCssVariable('--color-gray-500'), 0.25),
          fill: false,
          borderWidth: 2,
          pointRadius: 0,
          pointHoverRadius: 3,
          pointBackgroundColor: adjustColorOpacity(getCssVariable('--color-gray-500'), 0.25),
          pointHoverBackgroundColor: adjustColorOpacity(getCssVariable('--color-gray-500'), 0.25),
          pointBorderWidth: 0,
          pointHoverBorderWidth: 0,               
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