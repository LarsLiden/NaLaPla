namespace NaLaPla
{
    public class TaskList {
        public List<string> taskDescriptions = new List<string>();

        // Was this task list loaded from the cache
        public bool fromCache = false;

        // Number of times this task list was selected as the best
        public int bestCount = 0;

        // For JSON deserialization need plain contructor
        public TaskList() { }

        public TaskList(string sourceString) {

            // TODO: can this util function exist only here?
            this.taskDescriptions = Util.ParseSubPlanList(sourceString);
        }

        public TaskList(List<string> taskDescriptions) {
            this.taskDescriptions = taskDescriptions;
        }

        public override string ToString() {
            var output = "";
            for (int i = 0; i < taskDescriptions.Count; i++) {
                output += $"{i+1}. {taskDescriptions.ElementAt(i)}\n";
            }
            return output;
        }

        public static TaskList Find(TaskList taskList, List<TaskList> taskLists) {
            foreach (var testList in taskLists) {
                if (Equal(testList, taskList)) {
                    return testList;
                }
            }
            return null;
        }

        public static bool Equal(TaskList list1, TaskList list2) {
            if (list1.taskDescriptions.Count != list2.taskDescriptions.Count) {
                    return false;
                }
                for (int i=0;i<list1.taskDescriptions.Count;i++) {
                    if (!string.Equals(list1.taskDescriptions.ElementAt(i), list2.taskDescriptions.ElementAt(i), StringComparison.CurrentCultureIgnoreCase)) {
                        return false;
                    }
                }
                return true;
        }

        public static List<TaskList> RemoveDuplicates(List<TaskList> taskLists) {
            var cleanTaskList = new List<TaskList>();

            foreach (var taskList in taskLists) {
                TaskList foundMatch = null;
                foreach (var test in cleanTaskList) {
                    if (Equal(test, taskList)) {
                        foundMatch = test;
                        Equal(test, taskList);  // TODO temp
                    }
                }
                if (foundMatch == null) {
                    cleanTaskList.Add(taskList);
                }
                else {
                    foundMatch.bestCount = Math.Max(foundMatch.bestCount, taskList.bestCount);
                    foundMatch.fromCache = foundMatch.fromCache || taskList.fromCache;
                }
            }
            return cleanTaskList;
        }
    }
}