namespace NaLaPla
{
    public class RuntimeConfig {
        public int maxConcurrentGPTRequests = 1;
        public bool showPrompts = false; // whether or not to print each prompt as it is submitted to GPT. Prompts always stored in plan.prompt.
        public bool showResults = false; // print the parsed result of each request to the console

        public int expandDepth = 2;
        public float temperature = 0.2f;
        public float tempMultPerLevel = 1.0f;
        public string subtaskCount = "";  // Default to not specifying number of sub-tasks.  Creates less noise

        public override string ToString() {
            var stringified = $"expandDepth = {expandDepth}, subtaskCount = {subtaskCount},"
            + $" default temperature = {temperature}, temperature multiplier per level = {tempMultPerLevel}, maxConcurrentGPTRequests = {maxConcurrentGPTRequests},"
            + $" showPrompts = {showPrompts}, showResults = {showResults}";
            return stringified;
        }
    }
}