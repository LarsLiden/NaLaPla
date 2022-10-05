namespace NaLaPla
{
    public enum ExpandModeType
    {
        // Expand sub-tasks one at a time
        ONE_BY_ONE,

        // Expand all sub-tasks at the same time
        AS_A_LIST    
    }

    public class RuntimeConfig {

        public ExpandModeType ExpandMode = ExpandModeType.ONE_BY_ONE;

        public int maxConcurrentGPTRequests = 1;

        // whether or not to print each prompt as it is submitted to GPT. Prompts always stored in plan.prompt.
        public bool showPrompts = true; 
        
        // print the parsed result of each request to the console
        public bool showResults = true; 

        // Show document retrieval when grounding is employed
        public bool showGrounding = true;

        // Should grounding be added to prompts
        public bool useGrounding = true;

        public int expandDepth = 2;
        public float temperature = 0.1f;
        public float tempMultPerLevel = 1.0f;
        public string subtaskCount = "";  // Default to not specifying number of sub-tasks.  Creates less noise

        public bool shouldLoadPlan = false;

        public string indexToBuild = "";

        public override string ToString() {
            var stringified = $"expandDepth = {expandDepth}, subtaskCount = {subtaskCount},"
            + $" default temperature = {temperature}, temperature multiplier per level = {tempMultPerLevel}, maxConcurrentGPTRequests = {maxConcurrentGPTRequests},"
            + $" showPrompts = {showPrompts}, showResults = {showResults}, showGrounding = {showGrounding}, useGrounding = {useGrounding}, shouldLoadPlan = {shouldLoadPlan}, indexToBuild = {indexToBuild}";
            return stringified;
        }
    }
}