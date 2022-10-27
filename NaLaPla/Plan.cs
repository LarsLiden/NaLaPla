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
        public List<string> subPlanDescriptions = new List<string>();    
        public List<Plan> subPlans;
        public Plan? parent;
        public PlanState state = PlanState.CREATED;
        public Prompt? prompt;

        public List<string> candidateSubPlans;

        public Plan(string description, Plan parent) {
            this.description = description;
            this.planLevel = parent == null ? 0 : parent.planLevel;
            this.parent = parent;
            this.subPlans = new List<Plan>();
        }

        // Convert list of plan subtasks into a list
        public string GetNumberedSubTasksAsString() {
                var list = "START LIST\n";
                list += Util.GetNumberedSteps(this);
                list += "END LIST";
                return list;
        }
    }
}