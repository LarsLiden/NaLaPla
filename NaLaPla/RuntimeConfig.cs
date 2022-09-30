namespace NaLaPla
{
    public class RuntimeConfig {
        public int maxConcurrentGPTRequests = 25;

        // whether or not to print each prompt as it is submitted to GPT. Prompts always stored in plan.prompt.
        public bool showPrompts = false; 
        
        // print the parsed result of each request to the console
        public bool showResults = false; 

        // Show document retrieval when grounding is employed
        public bool showGrounding = false;

        // Should grounding be added to prompts
        public bool useGrounding = true;

        public int expandDepth = 2;
        public float temperature = 0.2f;
        public float tempMultPerLevel = 1.0f;
        public string subtaskCount = "";  // Default to not specifying number of sub-tasks.  Creates less noise

        public bool shouldLoadPlan = true;

        public string indexToBuild = "";

        public override string ToString() {
            var stringified = $"expandDepth = {expandDepth}, subtaskCount = {subtaskCount},"
            + $" default temperature = {temperature}, temperature multiplier per level = {tempMultPerLevel}, maxConcurrentGPTRequests = {maxConcurrentGPTRequests},"
            + $" showPrompts = {showPrompts}, showResults = {showResults}, showGrounding = {showGrounding}, useGrounding = {useGrounding}, shouldLoadPlan = {shouldLoadPlan}, indexToBuild = {indexToBuild}";
            return stringified;
        }
    }
}