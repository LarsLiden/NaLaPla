namespace NaLaPla
{

    public class DisplayOptions {

        // Show plan progress at every step
        public bool showProgress = false;

        // whether or not to print each prompt as it is submitted to GPT. Prompts always stored in plan.prompt.
        public bool showPrompts = false; 
        
        // print the parsed result of each request to the console
        public bool showResults = false; 

        // Show document retrieval when grounding is employed
        public bool showGrounding = false;

        public override string ToString() {
            var stringified = $" showPrompts = {showPrompts}, showResults = {showResults}, showGrounding = {showGrounding}";
            return stringified;
        }
    }
}