namespace NaLaPla
{
    public class RuntimeConfig {
        public bool parallelGPTRequests = true;
        public bool showPrompts = false; // whether or not to print each prompt as it is submitted to GPT. Prompts always stored in plan.prompt.
        public bool showResults = false; // print the parsed result of each request to the console

        public int expandDepth = 2;
        public string subtaskCount = "four";

        public override string ToString() {
            var stringified = $"expandDepth = {expandDepth}, subtaskCount = {subtaskCount}, parallelGPTRequests = {parallelGPTRequests}, showPrompts = {showPrompts}, showResults = {showResults}";
            return stringified;
        }
    }
}