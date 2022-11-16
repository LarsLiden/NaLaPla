namespace NaLaPla
{      
    public enum PromptType
    {
        // First prompt for base plan
        FIRSTPLAN,

        // Prompt based on adding subtasks to current task description
        TASK,

        // Prompt based on adding subtasks for each task description in a list
        TASKLIST
    }

    public enum BestTaskPromptType
    {
        // Show full plan and ask which is best 
        FULL,

        // Show partial plan and ask which is best
        PARTIAL
    }

    public class Prompt {

        public const int MAX_PROMPT_SIZE = 2000;

        public string text = "";

        public List<string> responses = new List<string>();

        public OpenAIConfig OAIConfig = new OpenAIConfig();

        public Prompt(string text, OpenAIConfig openAIConfig) {
            this.text = text;
            this.OAIConfig = openAIConfig;
        }

        public Prompt() {
        }

        public override string ToString()
        {
            return text;
        }
    }

    public class ExpandPrompt : Prompt {

        private static string? _oneshot;

        private static string oneShot {
            get {
                if (_oneshot == null) {
                    var promptFileName = Path.Combine(Environment.CurrentDirectory, "OneShotExpandPrompt.txt");
                    _oneshot = File.ReadAllText(promptFileName);
                }
                return _oneshot;
            }
        }

        private static string MakeBasePrompt(Plan plan, PromptType promptType, bool useGrounding, string requestNumberOfSubtasks, bool displayGrounding) {

            var promptText = "";
            var irKey = "";
            var numSubtasks = requestNumberOfSubtasks != "" ? $"{requestNumberOfSubtasks} " : "";
            switch (promptType) {
                case PromptType.FIRSTPLAN:

                    
                    promptText  +=  $"Your job is to provide instructions for a computer agent to {plan.description}. Please specify a numbered list of {numSubtasks}brief tasks that needs to be done.";
                    irKey = plan.description;
                    break;
                case PromptType.TASK:
                    // Other experimental versions
                    // var prompt =  $"Your job is to {plan.parent.description}. Your current task is to {plan.description}. Please specify a numbered list of the work that needs to be done.";
                    //var prompt = $"Please specify a numbered list of the work that needs to be done to {plan.description} when you {plan.root.description}";
                    //var prompt = $"Please specify one or two steps that needs to be done to {plan.description} when you {plan.root.description}";
                    //text = $"Your task is to {description}. Repeat the list and add {runtimeConfiguration.subtaskCount} subtasks to each of the items.\n\n";

                    promptText = "";// Util.PlanToString(plan.root);
                    promptText += $"\nProvide a list of short actions for a computer agent to {plan.description} in MineCraft.\n\n";
                    irKey = plan.description;
                    break;
                case PromptType.TASKLIST:
                    /* Other experimental version
                    var prompt =  $"Your job is to {plan.description}. You have identified the following steps:\n";
                    prompt += Util.GetNumberedSteps(plan);
                    prompt += "Please specify a bulleted list of the work that needs to be done for each step.";
                    */
                    promptText = String.Copy(oneShot);
                    var description = (plan.root == null || plan.root.description is null) ? "fire your lead developer" : plan.root.description;
                    var numberedSubTasksAsString = plan.SubPlanDescriptions();
    
                    promptText  += $"Below are instruction for a computer agent to {description}. Repeat the list and insert {numSubtasks}bulleted subtasks under each of the numbered items.\n\n";// in cases where the computer agent could use detail\n\n";
                    promptText  += numberedSubTasksAsString;
                    irKey = numberedSubTasksAsString;
                    break;
            }

            // Now add grounding 
            if (useGrounding) {
                var promptSize = Util.NumWordsIn(promptText);
                var maxGrounds = MAX_PROMPT_SIZE - promptSize;

                var documents = IR.GetRelatedDocuments($"{irKey}", displayGrounding);
                var grounding = "";
                foreach (var document in documents) {
                    grounding += $"{document}{Environment.NewLine}";
                }
                grounding = Util.LimitWordCountTo(grounding, maxGrounds);
                promptText = $"{grounding}{Environment.NewLine}{promptText}";
            }
            return promptText;
        }

        public ExpandPrompt(Plan plan, PromptType promptType, bool useGrounding, string requestNumberOfSubtasks, bool displayGrounding) 
        {
            text = MakeBasePrompt(plan, promptType, useGrounding, requestNumberOfSubtasks, displayGrounding);
        }   
    }
}