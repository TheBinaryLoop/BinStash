<template>
  <div class="bg-white dark:bg-gray-900">
    <div>

      <!-- Table -->
      <div class="overflow-x-auto">
        <table class="table-auto w-full dark:text-gray-300">
          <!-- Table header -->
          <thead class="text-xs font-semibold uppercase text-gray-500 border-t border-b border-gray-200 dark:border-gray-700/60">
            <tr>
              <th class="px-2 first:pl-5 last:pr-5 py-3 whitespace-nowrap w-px">
                <div class="flex items-center">
                  <label class="inline-flex">
                    <span class="sr-only">Select all</span>
                    <input id="parent-checkbox" class="form-checkbox" type="checkbox" @click="checkAll" />
                  </label>
                </div>
              </th>
              <th class="px-2 first:pl-5 last:pr-5 py-3 whitespace-nowrap">
                <div class="font-semibold text-left">Counterparty</div>
              </th>
              <th class="px-2 first:pl-5 last:pr-5 py-3 whitespace-nowrap">
                <div class="font-semibold text-left">Payment Date</div>
              </th>
              <th class="px-2 first:pl-5 last:pr-5 py-3 whitespace-nowrap">
                <div class="font-semibold text-left">Status</div>
              </th>
              <th class="px-2 first:pl-5 last:pr-5 py-3 whitespace-nowrap">
                <div class="font-semibold text-right">Amount</div>
              </th>
            </tr>
          </thead>
          <!-- Table body -->
          <tbody class="text-sm divide-y divide-gray-100 dark:divide-gray-700/60 border-b border-gray-200 dark:border-gray-700/60">
            <TransactionItem
              v-for="transaction in transactions"
              :key="transaction.id"
              :transaction="transaction"
              v-model:selected="selected"
              :value="transaction.id"
              @click.stop="$emit('open-transactionpanel')"
            />
          </tbody>
        </table>

      </div>
    </div>
  </div>
</template>

<script>
import { ref, watch } from 'vue'
import TransactionItem from './TransactionsTableItem02.vue'

import Image01 from '../../images/transactions-image-01.svg'
import Image02 from '../../images/transactions-image-02.svg'
import Image03 from '../../images/user-36-05.jpg'
import Image04 from '../../images/transactions-image-03.svg'
import Image05 from '../../images/transactions-image-04.svg'
import Image06 from '../../images/transactions-image-05.svg'
import Image07 from '../../images/transactions-image-06.svg'
import Image08 from '../../images/transactions-image-07.svg'
import Image09 from '../../images/transactions-image-08.svg'

export default {
  name: 'TransactionsTable02',
  components: {
    TransactionItem,
  },  
  props: ['selectedItems'],
  setup(props, { emit }) {

    const selectAll = ref(false)
    const selected = ref([])

    const checkAll = () => {
      selected.value = []
      if (!selectAll.value) {
        selected.value = transactions.value.map(transaction => transaction.id)
      }
    }
    
    watch(selected, () => {
      selectAll.value = transactions.value.length === selected.value.length ? true : false
      emit('change-selection', selected.value)
    })    
    
    const transactions = ref([
      {
        id: '0',
        image: Image01,
        name: 'Form Builder CP',
        date: '22/01/2024',
        status: 'Pending',
        amount: '-$1,299.22',
      },
      {
        id: '1',
        image: Image02,
        name: 'Imperial Hotel ****',
        date: '22/01/2024',
        status: 'Completed',
        amount: '-$1,029.77',
      },   
      {
        id: '2',
        image: Image03,
        name: 'Aprilynne Pills',
        date: '22/01/2024',
        status: 'Pending',
        amount: '+$499.99',
      },   
      {
        id: '3',
        image: Image04,
        name: 'Google Limited UK',
        date: '22/01/2024',
        status: 'Completed',
        amount: '-$1,029.77',
      },   
      {
        id: '4',
        image: Image05,
        name: 'Acme LTD UK',
        date: '22/01/2024',
        status: 'Pending',
        amount: '+$2,179.36',
      },   
      {
        id: '5',
        image: Image04,
        name: 'Google Limited UK',
        date: '22/01/2024',
        status: 'Canceled',
        amount: '-$1,029.77',
      },   
      {
        id: '6',
        image: Image06,
        name: 'Uber',
        date: '22/01/2024',
        status: 'Completed',
        amount: '-$272.88',
      },   
      {
        id: '7',
        image: Image07,
        name: 'PublicOne Inc.',
        date: '22/01/2024',
        status: 'Completed',
        amount: '-$199.87',
      },   
      {
        id: '8',
        image: Image08,
        name: 'Github Inc.',
        date: '22/01/2024',
        status: 'Completed',
        amount: '-$42.87',
      },   
      {
        id: '9',
        image: Image09,
        name: 'Form Builder PRO',
        date: '22/01/2024',
        status: 'Completed',
        amount: '-$112.44',
      },                                                      
    ])

    return {
      selectAll,
      selected,
      checkAll,
      transactions,
    }
  }
}
</script>