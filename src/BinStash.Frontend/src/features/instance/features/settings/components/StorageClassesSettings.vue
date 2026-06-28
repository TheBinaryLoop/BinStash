<template>
  <div class="space-y-6">
    <!-- Header + Add button -->
    <div class="flex items-center justify-between">
      <div>
        <h2 class="text-lg font-semibold text-slate-900 dark:text-white">Storage Classes</h2>
        <p class="text-sm text-slate-500 dark:text-slate-400 mt-0.5">
          Define named storage tiers that repositories can be assigned to.
        </p>
      </div>
      <button
        @click="showAddForm = true"
        class="inline-flex items-center gap-2 rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-[#7C86FF]/20 transition hover:bg-[#6d78ff]"
      >
        <IconPlus class="w-4 h-4" />
        Add Storage Class
      </button>
    </div>

    <!-- Loading -->
    <div v-if="loading" class="flex items-center gap-3 text-slate-500 dark:text-slate-400 py-8 justify-center">
      <Spinner />
      <span>Loading storage classes…</span>
    </div>

    <!-- Error -->
    <div v-else-if="error" class="rounded-[28px] border border-rose-200 bg-rose-50 p-4 text-sm text-rose-700 dark:border-rose-500/20 dark:bg-rose-500/10 dark:text-rose-400">
      {{ error }}
    </div>

    <!-- Empty state -->
    <div
      v-else-if="storageClasses.length === 0 && !showAddForm"
      class="rounded-[28px] border border-dashed border-slate-300 bg-white p-10 text-center shadow-sm dark:border-white/10 dark:bg-[#0F172D]"
    >
      <div class="w-12 h-12 rounded-full bg-slate-100 dark:bg-white/5 flex items-center justify-center mx-auto mb-3">
        <IconStack class="text-slate-400 w-6 h-6" />
      </div>
      <p class="text-slate-600 dark:text-slate-400 font-medium">No storage classes yet</p>
      <p class="text-sm text-slate-500 dark:text-slate-500 mt-1">Add a storage class to get started.</p>
    </div>

    <!-- Add form -->
    <div v-if="showAddForm" class="rounded-[28px] border border-[#7C86FF]/30 bg-white p-5 shadow-sm dark:border-[#7C86FF]/20 dark:bg-[#0F172D]">
      <h3 class="font-semibold text-slate-900 dark:text-white mb-4">New Storage Class</h3>
      <div class="grid grid-cols-1 sm:grid-cols-3 gap-4">
        <div>
          <label class="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Name <span class="text-rose-500">*</span></label>
          <input
            v-model="newClass.name"
            type="text"
            placeholder="e.g. standard"
            class="form-input w-full text-sm"
            :class="{ 'border-rose-300 dark:border-rose-500/50': addError && !newClass.name }"
          />
        </div>
        <div>
          <label class="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Display Name <span class="text-rose-500">*</span></label>
          <input
            v-model="newClass.displayName"
            type="text"
            placeholder="e.g. Standard"
            class="form-input w-full text-sm"
            :class="{ 'border-rose-300 dark:border-rose-500/50': addError && !newClass.displayName }"
          />
        </div>
        <div>
          <label class="block text-xs font-medium text-slate-500 dark:text-slate-400 mb-1">Description</label>
          <input
            v-model="newClass.description"
            type="text"
            placeholder="Optional description"
            class="form-input w-full text-sm"
          />
        </div>
      </div>
      <div v-if="addError" class="mt-2 text-xs text-rose-600 dark:text-rose-400">{{ addError }}</div>
      <div class="flex items-center gap-3 mt-4">
        <button
          @click="saveNewClass"
          :disabled="saving"
          class="rounded-full bg-[#7C86FF] px-4 py-2.5 text-sm font-semibold text-white shadow-lg shadow-[#7C86FF]/20 transition hover:bg-[#6d78ff] disabled:opacity-60"
        >
          {{ saving ? 'Saving…' : 'Save' }}
        </button>
        <button
          @click="cancelAdd"
          class="rounded-full border border-slate-200 dark:border-white/10 px-4 py-2.5 text-sm font-medium text-slate-700 dark:text-slate-300 hover:bg-slate-50 dark:hover:bg-white/[0.03] transition"
        >
          Cancel
        </button>
      </div>
    </div>

    <!-- Storage class list -->
    <div v-if="!loading && storageClasses.length > 0" class="overflow-hidden rounded-[28px] border border-slate-200 bg-white shadow-sm dark:border-white/5 dark:bg-[#0F172D]">
      <table class="w-full text-sm">
        <thead>
          <tr class="text-xs font-semibold text-slate-400 dark:text-slate-500 uppercase tracking-wide bg-slate-50 dark:bg-white/[0.03] border-b border-slate-200 dark:border-white/5">
            <th class="px-5 py-3 text-left">Name</th>
            <th class="px-5 py-3 text-left">Display Name</th>
            <th class="px-5 py-3 text-left">Description</th>
          </tr>
        </thead>
        <tbody class="divide-y divide-slate-100 dark:divide-white/5">
          <tr
            v-for="sc in storageClasses"
            :key="sc.name"
            class="hover:bg-slate-50 dark:hover:bg-white/[0.03] transition"
          >
            <td class="px-5 py-3 font-mono text-xs font-medium text-[#7C86FF]">
              {{ sc.name }}
            </td>
            <td class="px-5 py-3 font-medium text-slate-900 dark:text-white">
              {{ sc.displayName }}
            </td>
            <td class="px-5 py-3 text-slate-500 dark:text-slate-400">
              {{ sc.description || '—' }}
            </td>
          </tr>
        </tbody>
      </table>
    </div>

    <!-- Success toast -->
    <div
      v-if="successMsg"
      class="fixed bottom-4 right-4 z-50 bg-green-500 text-white text-sm font-medium px-4 py-2.5 rounded-xl shadow-lg flex items-center gap-2 animate-fade-in"
    >
      <IconCircleCheck class="w-4 h-4 shrink-0" />
      {{ successMsg }}
    </div>
  </div>
</template>

<script lang="ts" setup>
import { ref, onMounted } from 'vue'
import { IconPlus, IconStack, IconCircleCheck } from '@tabler/icons-vue'
import {
  listStorageClasses,
  createStorageClasses,
  type StorageClassDto,
} from '@/api/storageClasses'
import Spinner from '@/shared/components/feedback/Spinner.vue'

const loading = ref(true)
const error = ref<string | null>(null)
const saving = ref(false)
const addError = ref<string | null>(null)
const successMsg = ref<string | null>(null)
const showAddForm = ref(false)

const storageClasses = ref<StorageClassDto[]>([])

const newClass = ref<StorageClassDto>({ name: '', displayName: '', description: '', isDeprecated: false })

async function load() {
  loading.value = true
  error.value = null
  try {
    storageClasses.value = await listStorageClasses()
  } catch (e: any) {
    error.value = e.message || 'Failed to load storage classes.'
  } finally {
    loading.value = false
  }
}

function cancelAdd() {
  showAddForm.value = false
  addError.value = null
  newClass.value = { name: '', displayName: '', description: '', isDeprecated: false }
}

async function saveNewClass() {
  addError.value = null
  if (!newClass.value.name.trim()) {
    addError.value = 'Name is required.'
    return
  }
  if (!newClass.value.displayName.trim()) {
    addError.value = 'Display Name is required.'
    return
  }
  saving.value = true
  try {
    // The API adds to existing classes, so we send all current + new
    const allClasses = [
      ...storageClasses.value,
      { ...newClass.value },
    ]
    await createStorageClasses(allClasses)
    await load()
    cancelAdd()
    showSuccess('Storage class created successfully.')
  } catch (e: any) {
    addError.value = e.message || 'Failed to create storage class.'
  } finally {
    saving.value = false
  }
}

function showSuccess(msg: string) {
  successMsg.value = msg
  setTimeout(() => (successMsg.value = null), 3000)
}

onMounted(load)
</script>