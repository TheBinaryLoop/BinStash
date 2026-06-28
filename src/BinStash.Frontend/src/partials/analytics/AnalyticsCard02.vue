<template>
  <div class="flex flex-col col-span-full xl:col-span-4 bg-white dark:bg-gray-800 shadow-xs rounded-xl">
    <header class="px-5 py-4 border-b border-gray-100 dark:border-gray-700/60">
      <h2 class="font-semibold text-gray-800 dark:text-gray-100">Active Users Right Now</h2>
    </header>
    <!-- Card content -->
    <div class="flex flex-col h-full">
      <!-- Live visitors number -->
      <div class="px-5 py-3">
        <div class="flex items-center">
          <!-- Red dot -->
          <div class="relative flex items-center justify-center w-3 h-3 mr-3" aria-hidden="true">
            <div class="animate-ping absolute inline-flex h-full w-full rounded-full bg-red-400 opacity-50"></div>
            <div class="relative inline-flex rounded-full w-1.5 h-1.5 bg-red-500"></div>
          </div> 
          <!-- Vistors number -->
          <div>
            <div class="text-3xl font-bold text-gray-800 dark:text-gray-100 mr-2">347</div>
            <div class="text-sm text-gray-500 dark:text-gray-400">Live visitors</div>
          </div>
        </div>
      </div>

      <!-- Chart built with Chart.js 3 -->
      <div >
        <!-- Change the height attribute to adjust the chart height -->
        <LineChart :data="chartData" width="389" height="70" />
      </div>

      <!-- Table -->
      <div class="grow px-5 pt-3 pb-1">
        <div class="overflow-x-auto">
          <table class="table-auto w-full dark:text-gray-300">
            <!-- Table header -->
            <thead class="text-xs uppercase text-gray-400 dark:text-gray-500">
              <tr>
                <th class="py-2">
                  <div class="font-semibold text-left">Top pages</div>
                </th>
                <th class="py-2">
                  <div class="font-semibold text-right">Active users</div>
                </th>
              </tr>
            </thead>
            <!-- Table body -->
            <tbody class="text-sm divide-y divide-gray-100 dark:divide-gray-700/60">
              <!-- Row -->
              <tr>
                <td class="py-2">
                  <div class="text-left">preview.cruip.com/open-pro/</div>
                </td>
                <td class="py-2">
                  <div class="font-medium text-right text-gray-800">94</div>
                </td>
              </tr>
              <!-- Row -->
              <tr>
                <td class="py-2">
                  <div class="text-left">preview.cruip.com/simple/</div>
                </td>
                <td class="py-2">
                  <div class="font-medium text-right text-gray-800">42</div>
                </td>
              </tr>
              <!-- Row -->
              <tr>
                <td class="py-2">
                  <div class="text-left">cruip.com/unlimited/</div>
                </td>
                <td class="py-2">
                  <div class="font-medium text-right text-gray-800">12</div>
                </td>
              </tr>
              <!-- Row -->
              <tr>
                <td class="py-2">
                  <div class="text-left">preview.cruip.com/twist/</div>
                </td>
                <td class="py-2">
                  <div class="font-medium text-right text-gray-800">4</div>
                </td>
              </tr>
            </tbody>
          </table>
        </div>
      </div>

      <!-- Card footer -->
      <div class="text-right px-5 pb-4">
        <router-link class="text-sm font-medium text-violet-500 hover:text-violet-600 dark:hover:text-violet-400" to="#0">Real-Time Report -&gt;</router-link>
      </div>
    </div>
  </div>
</template>

<script>
import { ref } from 'vue'
import { chartAreaGradient } from '../../charts/ChartjsConfig'
import LineChart from '../../charts/LineChart04.vue'

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
          data: [
            732, 610, 610, 504, 504, 504, 349,
            349, 504, 342, 504, 610, 391, 192,
            154, 273, 191, 191, 126, 263, 349,
            252, 423, 622, 470, 532,
          ],
          fill: true,
          backgroundColor: function(context) {
            const chart = context.chart;
            const {ctx, chartArea} = chart;
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
      ],
    })

    return {
      chartData,
    } 
  }
}
</script>