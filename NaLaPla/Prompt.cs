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

    public class Prompt {

        public const int MAX_PROMPT_SIZE = 2000;

        public string text = "";

        public List<string> responses = new List<string>();

        public OpenAIConfig OAIConfig = new OpenAIConfig();

        public Prompt(string text,RuntimeConfig? configuration) {
            this.text = text;
            if (configuration is not null) {
                this.OAIConfig.Temperature = configuration.temperature;
            }
        }

        public Prompt() {
        }

        public override string ToString()
        {
            return text;
        }
    }

    public class ExpandPrompt : Prompt {

        private static string MakeBasePrompt(Plan basePlan, Plan plan, PromptType promptType, bool useGrounding, string requestNumberOfSubtasks, bool displayGrounding) {

            var promptText = "";
            var irKey = "";
            switch (promptType) {
                case PromptType.FIRSTPLAN:
                    promptText  =  $"Your job is to provide instructions for a computer agent to {plan.description}. Please specify a numbered list of {requestNumberOfSubtasks} brief tasks that needs to be done.";
                    irKey = plan.description;
                    break;
                case PromptType.TASK:
                    // Other experimental versions
                    // var prompt =  $"Your job is to {plan.parent.description}. Your current task is to {plan.description}. Please specify a numbered list of the work that needs to be done.";
                    //var prompt = $"Please specify a numbered list of the work that needs to be done to {plan.description} when you {basePlan.description}";
                    //var prompt = $"Please specify one or two steps that needs to be done to {plan.description} when you {basePlan.description}";
                    //text = $"Your task is to {description}. Repeat the list and add {runtimeConfiguration.subtaskCount} subtasks to each of the items.\n\n";

                    promptText = "";// Util.PlanToString(basePlan);
                    promptText += $"\nProvide a list of short actions for a computer agent to {plan.description} in MineCraft.\n\n";
                    irKey = plan.description;
                    break;
                case PromptType.TASKLIST:
                    /* Other experimental version
                    var prompt =  $"Your job is to {plan.description}. You have identified the following steps:\n";
                    prompt += Util.GetNumberedSteps(plan);
                    prompt += "Please specify a bulleted list of the work that needs to be done for each step.";
                    */
                    var description = (basePlan is null || basePlan.description is null) ? "fire your lead developer" : basePlan.description;
                    var numberedSubTasksAsString = plan.SubPlanDescriptions();
    
                    promptText  = $"Below are instruction for a computer agent to {description}. Repeat the list and add {requestNumberOfSubtasks} subtasks to each of the items.\n\n";// in cases where the computer agent could use detail\n\n";
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

        public ExpandPrompt(Plan basePlan, Plan plan, PromptType promptType, bool useGrounding, string requestNumberOfSubtasks, bool displayGrounding) 
        {
            text = MakeBasePrompt(basePlan, plan, promptType, useGrounding, requestNumberOfSubtasks, displayGrounding);
        }   
    }
}