using System.ComponentModel;

namespace NaLaPla
{
    using System.IO;

    public static class PlanCache {

        private const string CACHE_FILENAME = "PlanCache";

        private static List<CachedPlan>? _cachedPlans;

        private static List<CachedPlan> cachedPlans {
            get {
                if (_cachedPlans == null) {
                    _cachedPlans = LoadCache();
                }
                return _cachedPlans;
            }
        }

        public static void AddNoExpandTaskList(string description) {
            var taskList = new TaskList();
            taskList.doNotExpand = true;
            AddTaskList(description, taskList);
        }

        public static void AddTaskList(string description, TaskList taskList) {

            // Find existing item
            var cachedPlan = cachedPlans.FirstOrDefault(c => c.description == description);
            if (cachedPlan == null) {
                cachedPlan = new CachedPlan(description);
                cachedPlans.Add(cachedPlan);
            }

            // Look for existing item
            var existingList = TaskList.Find(taskList, cachedPlan.candidateSubTasks);

            // If not found, create new one
            if (existingList == null) {
                cachedPlan.candidateSubTasks.Add(taskList);
                existingList = taskList;
                taskList.fromCache = true;
            }

            // Increase count of time it was selected
            taskList.bestCount++;
            SaveCache();
        }

        public static List<TaskList> GetTaskLists(string description) {
            var cachedPlan = cachedPlans.FirstOrDefault(c => c.description == description);
            if (cachedPlan != null) {
                return cachedPlan.candidateSubTasks;
            }
            return new List<TaskList>();
        }

        private static List<CachedPlan> LoadCache() {
            var cacheString = Util.LoadText(CACHE_FILENAME);
            if (String.IsNullOrEmpty(cacheString)) {
                return new List<CachedPlan>();
            }   
            var cacheList = Newtonsoft.Json.JsonConvert.DeserializeObject<List<CachedPlan>>(cacheString);
            if (cacheList == null) {
                return new List<CachedPlan>();
            }
            return cacheList;
        }

        private static void SaveCache() {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(_cachedPlans);
            Util.SaveText(CACHE_FILENAME, json);
        }
    }

    public class CachedPlan {
        public string description = "";

        public List<TaskList> candidateSubTasks;

        public CachedPlan() {
            this.candidateSubTasks = new List<TaskList>();
        }

        public CachedPlan(string description) {
            this.description = description;
            this.candidateSubTasks = new List<TaskList>();
        }
    }
}