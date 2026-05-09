<template>
  <tr>
    <td class="px-2 first:pl-5 last:pr-5 py-3 whitespace-nowrap w-px">
      <div class="flex items-center">
        <label class="inline-flex">
          <span class="sr-only">Select</span>
          <input :id="transaction.id" class="form-checkbox" type="checkbox" :value="value" @change="check" :checked="checked" />
        </label>
      </div>
    </td>
    <td class="px-2 first:pl-5 last:pr-5 py-3 whitespace-nowrap md:w-1/2">
      <div class="flex items-center">
        <div class="w-9 h-9 shrink-0 mr-2 sm:mr-3">
          <img class="rounded-full" :src="transaction.image" width="36" height="36" :alt="transaction.name" />
        </div>
        <div class="font-medium text-gray-800 dark:text-gray-100">{{transaction.name}}</div>
      </div>
    </td>
    <td class="px-2 first:pl-5 last:pr-5 py-3 whitespace-nowrap">
      <div class="text-left">{{transaction.date}}</div>
    </td>
    <td class="px-2 first:pl-5 last:pr-5 py-3 whitespace-nowrap">
      <div class="text-left">
        <div class="text-xs inline-flex font-medium rounded-full text-center px-2.5 py-1" :class="statusColor(transaction.status)">{{transaction.status}}</div>
      </div>
    </td>
    <td class="px-2 first:pl-5 last:pr-5 py-3 whitespace-nowrap w-px">
      <div class="text-right font-medium" :class="amountColor(transaction.amount)">{{transaction.amount}}</div>
    </td>    
  </tr>  
</template>

<script>
import { computed } from 'vue'

export default {
  name: 'TransactionsTableItem',
  props: ['transaction', 'value', 'selected'],
  setup(props, context) {
    const checked = computed(() => props.selected.includes(props.value))

    function check() {
      let updatedSelected = [...props.selected]
      if (this.checked) {
        updatedSelected.splice(updatedSelected.indexOf(props.value), 1)
      } else {
        updatedSelected.push(props.value)
      }
      context.emit('update:selected', updatedSelected)
    }

    const statusColor = (status) => {
      switch (status) {
        case 'Completed':
          return 'bg-green-500/20 text-green-700'
        case 'Canceled':
          return 'bg-red-500/20 text-red-700'
        default:
          return 'bg-gray-400/20 text-gray-500 dark:text-gray-400'
      }
    }
    
    const amountColor = (amount) => {
      switch (amount.charAt(0)) {
        case '+':
          return 'text-green-500'
        default:
          return 'text-gray-800 dark:text-gray-300'
      }
    }    

    return {
      check,
      checked,
      statusColor,
      amountColor,
    }
  },
}
</script>