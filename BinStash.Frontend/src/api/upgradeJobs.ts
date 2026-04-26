/**
 * Backward-compatibility re-exports. New code should import from '@/api/backgroundJobs'.
 * Upgrade and rebuild jobs are now managed via the unified GraphQL backgroundJobs API.
 */
export {
  type BackgroundJobDto as UpgradeJobDto,
  listBackgroundJobs as listUpgradeJobs,
  cancelBackgroundJob as cancelUpgradeJob,
  getBackgroundJob as getUpgradeJob,
} from './backgroundJobs'
