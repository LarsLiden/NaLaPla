
namespace NaLaPla
{
    // A plans and the index of TaskList selected as the best response
    public class SamplePlan {

        public Plan? plan;

        // Index of TaskList that was chosen as the best response
        public string partialPlanPrompt;

        public string fullPlanPrompt;

        public SamplePlan(Plan plan) {

            // Make a copy and remove unneeded fields
            this.plan = Util.CopyObject(plan);

            if (this.plan == null) {
                throw new Exception("Failed to copy plan");
            }
            this.plan.subPlans = new List<Plan>();

            // Store plan prompt as save file won't store full plan
            this.fullPlanPrompt =  Util.MakeFullTaskPrompt(plan, BestTaskListExamples.WHICH_PROMPT);

            // Store plan prompt as save file won't store full plan
            this.partialPlanPrompt =  Util.MakePartialTaskPrompt(plan, BestTaskListExamples.WHICH_PROMPT);
        }
    }
}