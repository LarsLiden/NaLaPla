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
        public List<Plan> subPlans = new List<Plan>();
        public Plan? parent;
        public PlanState state = PlanState.CREATED;
        public Prompt? prompt;
        public string? GPTresponse;
    }
}