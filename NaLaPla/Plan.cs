
namespace NaLaPla
{
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

        // TODO: This field will not deserialize.  Need to add hooks to repopulate
        [Newtonsoft.Json.JsonIgnore]
        public Plan? root;

        // TODO: This field will not deserialize.  Need to add hooks to repopulate
        [Newtonsoft.Json.JsonIgnore]
        public Plan? parent;

        public PlanState state = PlanState.CREATED;

        public List<TaskList> candidateSubTasks;

        public Plan(string description, Plan? parent) {
            this.description = description;
            this.planLevel = parent == null ? 0 : parent.planLevel + 1;
            this.root = (parent == null || parent.root == null) ? parent : parent.root;
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

        public int BestTaskListIndex() {
            foreach (var taskList in this.candidateSubTasks) {
                if (taskList.ranking == 0) {
                    return this.candidateSubTasks.IndexOf(taskList);
                }
            }
            return -1;
        }


        // Output plan and sub-plans as a string
        public string PlanToString(bool showExpansionState = false) {

            return PlanToString(null, null, showExpansionState);
        }

        // Output plan and sub-plans as a string
        // If promptPlan is specified then show optionPrompt in place of the description
        public string PlanToString(Plan? promptPlan, string? optionPrompt, bool showExpansionState = false) {

            string planText; 
            if (this == promptPlan) 
            {
                planText = Util.IndentText($"  {optionPrompt}\n", planLevel + 1);
            } 
            else 
            {
                    var expansionStateString = showExpansionState ? $"({Util.EnumToDescription(state)})" : "";
                    planText = Util.IndentText($"- {description} {expansionStateString}\n", planLevel);
            };

            if (subPlans.Any()) {
                foreach (var subPlan in subPlans) {
                    planText += subPlan.PlanToString(promptPlan, optionPrompt, showExpansionState);
                }
            }
            return planText;
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