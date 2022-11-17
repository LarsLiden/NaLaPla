namespace NaLaPla
{

    public class DisplayOptions {
        // whether or not to print each prompt as it is submitted to GPT. Prompts always stored in plan.prompt.
        public bool showPrompts = true; 
        
        // print the parsed result of each request to the console
        public bool showResults = true; 

        // Show document retrieval when grounding is employed
        public bool showGrounding = true;

        public override string ToString() {
            var stringified = $" showPrompts = {showPrompts}, showResults = {showResults}, showGrounding = {showGrounding}";
            return stringified;
        }
    }
}