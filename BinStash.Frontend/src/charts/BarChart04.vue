<template>
  <div class="px-5 py-4">
    <ul ref="legend" class="flex flex-wrap gap-x-4"></ul>
  </div>
  <div class="grow">
    <canvas ref="canvas" :data="data" :width="width" :height="height"></canvas>
  </div>
</template>

<script>
import { ref, watch, onMounted, onUnmounted } from 'vue'
import { useDark } from '@vueuse/core'
import { getChartColors } from './ChartjsConfig'

import {
  Chart, BarController, BarElement, LinearScale, TimeScale, Tooltip, Legend,
} from 'chart.js'
import 'chartjs-adapter-moment'

// Import utilities
import { formatThousands } from '../utils/Utils'

Chart.register(BarController, BarElement, LinearScale, TimeScale, Tooltip, Legend)

export default {
  name: 'BarChart04',
  props: ['data', 'width', 'height'],
  setup(props) {

    const canvas = ref(null)
    const legend = ref(null)
    let chart = null
    const darkMode = useDark()
    const { textColor, gridColor, tooltipBodyColor, tooltipBgColor, tooltipBorderColor } = getChartColors()
    
    onMounted(() => {
      const ctx = canvas.value
      chart = new Chart(ctx, {
        type: 'bar',
        data: props.data,
        options: {
          indexAxis: 'y',
          layout: {
            padding: {
              top: 12,
              bottom: 16,
              left: 20,
              right: 20,
            },
          },
          scales: {
            y: {
              type: 'time',
              time: {
                parser: 'MM-DD-YYYY',
                unit: 'month',
                displayFormats: {
                  month: 'MMM',
                },
              },
              border: {
                display: false,
              },
              grid: {
                display: false,
              },
              ticks: {
                color: darkMode.value ? textColor.dark : textColor.light,
              },
            },
            x: {
              border: {
                display: false,
              },
              ticks: {
                maxTicksLimit: 3,
                align: 'end',
                callback: (value) => formatThousands(value),
                color: darkMode.value ? textColor.dark : textColor.light,
              },
              grid: {
                color: darkMode.value ? gridColor.dark : gridColor.light,
              },   
            },
          },
          plugins: {
            legend: {
              display: false,
            },
            tooltip: {
              callbacks: {
                title: () => false, // Disable tooltip title
                label: (context) => formatThousands(context.parsed.x),
              },
              bodyColor: darkMode.value ? tooltipBodyColor.dark : tooltipBodyColor.light,
              backgroundColor: darkMode.value ? tooltipBgColor.dark : tooltipBgColor.light,
              borderColor: darkMode.value ? tooltipBorderColor.dark : tooltipBorderColor.light,               
            },
          },
          interaction: {
            intersect: false,
            mode: 'nearest',
          },
          animation: {
            duration: 500,
          },
          maintainAspectRatio: false,
          resizeDelay: 200,
        },
        plugins: [{
          id: 'htmlLegend',
          afterUpdate(c, args, options) {
            const ul = legend.value
            if (!ul) return
            // Remove old legend items
            while (ul.firstChild) {
              ul.firstChild.remove()
            }
            // Reuse the built-in legendItems generator
            const items = c.options.plugins.legend.labels.generateLabels(c)
            items.forEach((item) => {
              const li = document.createElement('li')
              // Button element
              const button = document.createElement('button')
              button.style.display = 'inline-flex'
              button.style.alignItems = 'center'
              button.style.opacity = item.hidden ? '.3' : ''
              button.onclick = () => {
                c.setDatasetVisibility(item.datasetIndex, !c.isDatasetVisible(item.datasetIndex))
                c.update()
              }
              // Color box
              const box = document.createElement('span')
              box.style.display = 'block'
              box.style.width = '12px'
              box.style.height = '12px'
              box.style.borderRadius = 'calc(infinity * 1px)'
              box.style.marginRight = '8px'
              box.style.borderWidth = '3px'
              box.style.borderColor = item.fillStyle
              box.style.pointerEvents = 'none'
              // Label
              const label = document.createElement('span')
              label.classList.add('text-gray-500', 'dark:text-gray-400')
              label.style.fontSize = '14px'
              label.style.lineHeight = 'calc(1.25 / 0.875)'
              const labelText = document.createTextNode(item.text)
              label.appendChild(labelText)
              li.appendChild(button)
              button.appendChild(box)
              button.appendChild(label)
              ul.appendChild(li)
            })
          },
        }],
      })
    })

    onUnmounted(() => chart.destroy())

    watch(
      () => darkMode.value,
      () => {
        if (darkMode.value) {
          chart.options.scales.x.ticks.color = textColor.dark
          chart.options.scales.x.grid.color = gridColor.dark
          chart.options.scales.y.ticks.color = textColor.dark
          chart.options.plugins.tooltip.bodyColor = tooltipBodyColor.dark
          chart.options.plugins.tooltip.backgroundColor = tooltipBgColor.dark
          chart.options.plugins.tooltip.borderColor = tooltipBorderColor.dark
        } else {
          chart.options.scales.x.ticks.color = textColor.light
          chart.options.scales.x.grid.color = gridColor.light
          chart.options.scales.y.ticks.color = textColor.light
          chart.options.plugins.tooltip.bodyColor = tooltipBodyColor.light
          chart.options.plugins.tooltip.backgroundColor = tooltipBgColor.light
          chart.options.plugins.tooltip.borderColor = tooltipBorderColor.light
        }
        chart.update('none')
      })        

    return {
      canvas,
      legend,
    }
  }
}
</script>