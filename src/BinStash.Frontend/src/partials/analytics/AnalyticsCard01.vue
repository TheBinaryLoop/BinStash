<template>
  <div class="flex flex-col col-span-full xl:col-span-8 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60 flex items-center">
      <h2 class="font-semibold text-gray-800 dark:text-gray-100">Analytics</h2>
    </header>
    <div class="px-5 py-1">
      <div class="flex flex-wrap max-sm:*:w-1/2">
        <!-- Unique Visitors -->
        <div class="flex items-center py-2">
          <div class="mr-5">
            <div class="flex items-center">
              <div class="text-3xl font-bold text-gray-800 dark:text-gray-100 mr-2">24.7K</div>
              <div class="text-sm font-medium text-green-600">+49%</div>
            </div>
            <div class="text-sm text-gray-500 dark:text-gray-400">Unique Visitors</div>
          </div>
          <div class="hidden md:block w-px h-8 bg-gray-200 dark:bg-gray-700 mr-5" aria-hidden="true"></div>
        </div>
        <!-- Total Pageviews -->
        <div class="flex items-center py-2">
          <div class="mr-5">
            <div class="flex items-center">
              <div class="text-3xl font-bold text-gray-800 dark:text-gray-100 mr-2">56.9K</div>
              <div class="text-sm font-medium text-green-600">+7%</div>
            </div>
            <div class="text-sm text-gray-500 dark:text-gray-400">Total Pageviews</div>
          </div>
          <div class="hidden md:block w-px h-8 bg-gray-200 dark:bg-gray-700 mr-5" aria-hidden="true"></div>
        </div>
        <!-- Bounce Rate -->
        <div class="flex items-center py-2">
          <div class="mr-5">
            <div class="flex items-center">
              <div class="text-3xl font-bold text-gray-800 dark:text-gray-100 mr-2">54%</div>
              <div class="text-sm font-medium text-red-500">-7%</div>
            </div>
            <div class="text-sm text-gray-500 dark:text-gray-400">Bounce Rate</div>
          </div>
          <div class="hidden md:block w-px h-8 bg-gray-200 dark:bg-gray-700 mr-5" aria-hidden="true"></div>
        </div>
        <!-- Visit Duration-->
        <div class="flex items-center">
          <div>
            <div class="flex items-center">
              <div class="text-3xl font-bold text-gray-800 dark:text-gray-100 mr-2">2m 56s</div>
              <div class="text-sm font-medium text-red-500">+7%</div>
            </div>
            <div class="text-sm text-gray-500 dark:text-gray-400">Visit Duration</div>
          </div>
        </div>
      </div>
    </div>
    <!-- Chart built with Chart.js 3 -->
    <div class="grow">
      <!-- Change the height attribute to adjust the chart height -->
      <LineChart :data="chartData" width="800" height="300" />
    </div>
  </div>
</template>

<script>
import { ref } from 'vue'
import { chartAreaGradient } from '../../charts/ChartjsConfig'
import LineChart from '../../charts/LineChart03.vue'

// Import utilities
import { adjustColorOpacity, getCssVariable } from '../../utils/Utils'

export default {
  name: 'AnalyticsCard01',
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
        // Indigo line
        {
          label: 'Current',
          data: [
            5000, 8700, 7500, 12000, 11000, 9500, 10500,
            10000, 15000, 9000, 10000, 7000, 22000, 7200,
            9800, 9000, 10000, 8000, 15000, 12000, 11000,
            13000, 11000, 15000, 17000, 18000,
          ],
          fill: true,
          backgroundColor: function (context) {
            const chart = context.chart;
            const { ctx, chartArea } = chart;
            return chartAreaGradient(ctx, chartArea, [
              { stop: 0, color: adjustColorOpacity(getCssVariable('--color-violet-500'), 0) },
              { stop: 1, color: adjustColorOpacity(getCssVariable('--color-violet-500'), 0.2) }
            ]);
          },
          borderColor: getCssVariable('--color-violet-500'),
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
        // Gray line
        {
          label: 'Previous',
          data: [
            8000, 5000, 6500, 5000, 6500, 12000, 8000,
            9000, 8000, 8000, 12500, 10000, 10000, 12000,
            11000, 16000, 12000, 10000, 10000, 14000, 9000,
            10000, 15000, 12500, 14000, 11000,
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