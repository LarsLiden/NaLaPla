
namespace NaLaPla
{
    using System.Text.Json.Serialization;
    using System.ComponentModel;

    public enum PlanState {
        [Description("Created")]
        CREATED,
        [Description("Processing")]
        PROCESSING,
        [Description("GPT Request submitted")]
        GPT_PROMPT_SUBMITTED,
        [Description("GPT Response received")]
        GPT_RESPONSE_RECEIVED,
        [Description("Final leaf")]
        FINAL,
        [Description("Done")]
        DONE
    }

    public class Plan {
        public string description = "";
        public int planLevel;    
  
        public List<Plan> subPlans;

        // TODO: This field will not deseriaialize.  Need to add hooks to repopulate
        [Newtonsoft.Json.JsonIgnore]
        public Plan? parent;

        public PlanState state = PlanState.CREATED;

        public List<TaskList> candidateSubTasks;

        public Plan(string description, Plan parent) {
            this.description = description;
            this.planLevel = parent == null ? 0 : parent.planLevel + 1;
            this.parent = parent;
            this.subPlans = new List<Plan>();
            this.candidateSubTasks = new List<TaskList>();
        }

        // If plan doesn't have dontExpand option then add it
        // If exist, make sure it is the first option
        public void AddDoNotExpandOption() {
            var doNotExpandTaskList = this.candidateSubTasks.FirstOrDefault(tl => tl.doNotExpand == true);
            if (doNotExpandTaskList != null) {
                // Move item to front of list
                this.candidateSubTasks.Remove(doNotExpandTaskList);
                this.candidateSubTasks.Insert(0, doNotExpandTaskList);
            }
            else {
                // Add item to front of list
                doNotExpandTaskList = new TaskList();
                doNotExpandTaskList.doNotExpand = true;
                this.candidateSubTasks.Insert(0, doNotExpandTaskList);
            }
        }

        // Convert list of plan subtasks into a list for prompting
        public string SubPlanDescriptions() {
            if (subPlans.Count == 0) {
                return "";
            }
            var output = "START LIST\n";
            for (int i = 0; i < subPlans.Count; i++) {
                output += $"{i+1}. {subPlans.ElementAt(i).description}\n";
            }
            output += "END LIST";
            return output;
        }

        public string Reasoning() {
            var output = "";
            for (int i = 0; i < candidateSubTasks.Count; i++) {
                var taskList = candidateSubTasks.ElementAt(i);
                var reason = taskList.reason != null ? taskList.reason : "No reason";
                output+= $"<OPTION {i}> {reason}\n";
            }
            return output;
        }
    }
}