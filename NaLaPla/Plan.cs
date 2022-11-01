using System.ComponentModel;

namespace NaLaPla
{
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
    }
}