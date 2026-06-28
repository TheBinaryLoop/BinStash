<template>
  <div class="w-full md:w-60 mb-8 md:mb-0">
    <div class="md:sticky md:top-16 md:h-[calc(100dvh-64px)] md:overflow-x-hidden md:overflow-y-auto no-scrollbar">
      <div class="md:py-8">

        <!-- Title -->
        <header class="mb-6">
          <h1 class="text-2xl md:text-3xl text-gray-800 dark:text-gray-100 font-bold">Feed</h1>
        </header>

        <!-- Search form -->
        <div class="xl:hidden mb-6">
          <form class="relative">
            <label for="feed-search-mobile" class="sr-only">Search</label>
            <input id="feed-search-mobile" class="form-input w-full pl-9 bg-white dark:bg-gray-800" type="search" placeholder="Search…" />
            <button class="absolute inset-0 right-auto group" type="submit" aria-label="Search">
              <svg class="shrink-0 fill-current text-gray-400 dark:text-gray-500 group-hover:text-gray-500 dark:group-hover:text-gray-400 ml-3 mr-2" width="16" height="16" viewBox="0 0 16 16" xmlns="http://www.w3.org/2000/svg">
                <path d="M7 14c-3.86 0-7-3.14-7-7s3.14-7 7-7 7 3.14 7 7-3.14 7-7 7zM7 2C4.243 2 2 4.243 2 7s2.243 5 5 5 5-2.243 5-5-2.243-5-5-5z" />
                <path d="M15.707 14.293L13.314 11.9a8.019 8.019 0 01-1.414 1.414l2.393 2.393a.997.997 0 001.414 0 .999.999 0 000-1.414z" />
              </svg>
            </button>
          </form>
        </div>

        <!-- Links -->
        <div class="flex flex-nowrap overflow-x-scroll no-scrollbar md:block md:overflow-auto px-4 md:space-y-3 -mx-4">
          <!-- Group 1 -->
          <div>
            <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase mb-3 md:sr-only">Menu</div>
            <ul class="flex flex-nowrap md:block mr-3 md:mr-0">
              <li class="mr-0.5 md:mr-0 md:mb-0.5">
                <a class="flex items-center px-2.5 py-2 rounded-lg whitespace-nowrap bg-white dark:bg-gray-800" href="#0">
                  <svg class="shrink-0 fill-current text-violet-500 mr-2" width="16" height="16" viewBox="0 0 16 16">
                    <path d="M4.904 10.114a.98.98 0 0 1 0-1.961h5.886a.98.98 0 0 1 0 1.961H4.904ZM2.863 5.166a1.962 1.962 0 0 0-.901 1.651v5.26c0 1.083.878 1.961 1.961 1.961h7.85a1.962 1.962 0 0 0 1.961-1.961v-5.26c0-.668-.34-1.29-.901-1.65L7.848 1.961 2.863 5.166ZM6.786.312a1.962 1.962 0 0 1 2.123 0l4.985 3.204a3.923 3.923 0 0 1 1.802 3.301v5.26A3.923 3.923 0 0 1 11.772 16H3.923A3.923 3.923 0 0 1 0 12.077v-5.26c0-1.335.679-2.579 1.802-3.3L6.786.311Z" />
                  </svg>
                  <span class="text-sm font-medium text-violet-500">Home</span>
                </a>
              </li>
              <li class="mr-0.5 md:mr-0 md:mb-0.5">
                <a class="flex items-center px-2.5 py-2 rounded-lg whitespace-nowrap" href="#0">
                  <svg class="shrink-0 fill-current text-gray-400 dark:text-gray-500 mr-2" width="16" height="16" viewBox="0 0 16 16">
                    <path d="M8 3.414V6a1 1 0 1 1-2 0V1a1 1 0 0 1 1-1h5a1 1 0 0 1 0 2H9.414l6.293 6.293a1 1 0 1 1-1.414 1.414L8 3.414Zm0 9.172V10a1 1 0 1 1 2 0v5a1 1 0 0 1-1 1H4a1 1 0 0 1 0-2h2.586L.293 7.707a1 1 0 0 1 1.414-1.414L8 12.586Z" />
                  </svg>
                  <span class="text-sm font-medium text-gray-600 dark:text-gray-300">Explore</span>
                </a>
              </li>
              <li class="mr-0.5 md:mr-0 md:mb-0.5">
                <a class="flex items-center px-2.5 py-2 rounded-lg whitespace-nowrap" href="#0">
                  <svg class="shrink-0 fill-current text-gray-400 dark:text-gray-500 mr-2" width="16" height="16" viewBox="0 0 16 16">
                    <path d="M5 9a1 1 0 1 1 0-2h6a1 1 0 0 1 0 2H5ZM1 4a1 1 0 1 1 0-2h14a1 1 0 0 1 0 2H1Zm0 10a1 1 0 0 1 0-2h14a1 1 0 0 1 0 2H1Z" />
                  </svg>
                  <span class="text-sm font-medium text-gray-600 dark:text-gray-300">Notifications</span>
                </a>
              </li>
              <li class="mr-0.5 md:mr-0 md:mb-0.5">
                <a class="flex items-center px-2.5 py-2 rounded-lg whitespace-nowrap" href="#0">
                  <svg class="shrink-0 fill-current text-gray-400 dark:text-gray-500 mr-2" width="16" height="16" viewBox="0 0 16 16">
                    <path d="M13 4a2 2 0 0 0-2-2H5a2 2 0 0 0-2 2v9.131l4.445-2.963a1 1 0 0 1 1.11 0L13 13.13V4Zm-5 8.202-5.445 3.63A1 1 0 0 1 1 15V4a4 4 0 0 1 4-4h6a4 4 0 0 1 4 4v11a1 1 0 0 1-1.555.832L8 12.202Z" />
                  </svg>
                  <span class="text-sm font-medium text-gray-600 dark:text-gray-300">Bookmarks</span>
                </a>
              </li>
              <li class="mr-0.5 md:mr-0 md:mb-0.5">
                <a class="flex items-center px-2.5 py-2 rounded-lg whitespace-nowrap" href="#0">
                  <svg class="shrink-0 fill-current text-gray-400 dark:text-gray-500 mr-2" width="16" height="16" viewBox="0 0 16 16">
                    <path d="M8 9a4 4 0 1 1 0-8 4 4 0 0 1 0 8Zm0-2a2 2 0 1 0 0-4 2 2 0 0 0 0 4Zm-5.143 7.91a1 1 0 1 1-1.714-1.033A7.996 7.996 0 0 1 8 10a7.996 7.996 0 0 1 6.857 3.877 1 1 0 1 1-1.714 1.032A5.996 5.996 0 0 0 8 12a5.996 5.996 0 0 0-5.143 2.91Z" />
                  </svg>
                  <span class="text-sm font-medium text-gray-600 dark:text-gray-300">Profile</span>
                </a>
              </li>
            </ul>
          </div>
          <!-- Group 2 -->
          <div>
            <div class="text-xs font-semibold text-gray-400 dark:text-gray-500 uppercase mb-3">My Groups</div>
            <ul class="flex flex-nowrap md:block mr-3 md:mr-0">
              <li class="mr-0.5 md:mr-0 md:mb-0.5">
                <a class="flex items-center px-2.5 py-2 rounded-lg whitespace-nowrap" href="#0">
                  <svg class="shrink-0 fill-current text-gray-400 dark:text-gray-500 mr-2" width="16" height="16" viewBox="0 0 16 16">
                    <path d="M7.3 8.7c-.4-.4-.4-1 0-1.4l7-7c.4-.4 1-.4 1.4 0 .4.4.4 1 0 1.4l-7 7c-.4.4-1 .4-1.4 0ZM7.3 14.7c-.4-.4-.4-1 0-1.4l7-7c.4-.4 1-.4 1.4 0 .4.4.4 1 0 1.4l-7 7c-.4.4-1 .4-1.4 0ZM.3 9.7c-.4-.4-.4-1 0-1.4l7-7c.4-.4 1-.4 1.4 0 .4.4.4 1 0 1.4l-7 7c-.4.4-1 .4-1.4 0Z" />
                  </svg>
                  <span class="text-sm font-medium text-gray-600 dark:text-gray-300">Productivity</span>
                </a>
              </li>
              <li class="mr-0.5 md:mr-0 md:mb-0.5">
                <a class="flex items-center px-2.5 py-2 rounded-lg whitespace-nowrap" href="#0">
                  <svg class="shrink-0 fill-current text-gray-400 dark:text-gray-500 mr-2" width="16" height="16" viewBox="0 0 16 16">
                    <path d="M7.3 8.7c-.4-.4-.4-1 0-1.4l7-7c.4-.4 1-.4 1.4 0 .4.4.4 1 0 1.4l-7 7c-.4.4-1 .4-1.4 0ZM7.3 14.7c-.4-.4-.4-1 0-1.4l7-7c.4-.4 1-.4 1.4 0 .4.4.4 1 0 1.4l-7 7c-.4.4-1 .4-1.4 0ZM.3 9.7c-.4-.4-.4-1 0-1.4l7-7c.4-.4 1-.4 1.4 0 .4.4.4 1 0 1.4l-7 7c-.4.4-1 .4-1.4 0Z" />
                  </svg>
                  <span class="text-sm font-medium text-gray-600 dark:text-gray-300">Self Development</span>
                </a>
              </li>
            </ul>
          </div>
        </div>

      </div>
    </div>
  </div>
</template>

<script>
export default {
  name: 'FeedLeftContent',
}
</script>