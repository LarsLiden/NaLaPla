namespace NaLaPla
{      

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

        public ExpandPrompt(Plan basePlan, Plan plan, RuntimeConfig runtimeConfiguration) {

            string numberedSubTasksAsString = "";
            string irKey = "";
            if (plan.subPlanDescriptions.Count > 0) {
                numberedSubTasksAsString = plan.GetNumberedSubTasksAsString();
            }
            var description = (basePlan is null || basePlan.description is null) ? "fire your lead developer" : basePlan.description;
            if (plan.planLevel > 0  && runtimeConfiguration.ExpandMode == ExpandModeType.ONE_BY_ONE) {
                // var prompt =  $"Your job is to {plan.parent.description}. Your current task is to {plan.description}. Please specify a numbered list of the work that needs to be done.";
                //var prompt = $"Please specify a numbered list of the work that needs to be done to {plan.description} when you {basePlan.description}";
                //var prompt = $"Please specify one or two steps that needs to be done to {plan.description} when you {basePlan.description}";
                //text = $"Your task is to {description}. Repeat the list and add {runtimeConfiguration.subtaskCount} subtasks to each of the items.\n\n";

                text = "";// Util.PlanToString(basePlan);
                text += $"\nProvide a list of short actions for a computer agent to {plan.description} in MineCraft.\n\n";
                irKey = plan.description;
            }
            else if (plan.subPlanDescriptions.Count > 0 && runtimeConfiguration.ExpandMode == ExpandModeType.AS_A_LIST) {
                /*
                var prompt =  $"Your job is to {plan.description}. You have identified the following steps:\n";
                prompt += Util.GetNumberedSteps(plan);
                prompt += "Please specify a bulleted list of the work that needs to be done for each step.";
                */
                text  = $"Below are instruction for a computer agent to {description}. Repeat the list and add {runtimeConfiguration.promptSubtaskCount} subtasks to each of the items.\n\n";// in cases where the computer agent could use detail\n\n";
                text  += numberedSubTasksAsString;
                irKey = numberedSubTasksAsString;
            }
            else {
                text  =  $"Your job is to provide instructions for a computer agent to {plan.description}. Please specify a numbered list of {runtimeConfiguration.promptSubtaskCount} brief tasks that needs to be done.";
                irKey = plan.description;
            }
            
            if (runtimeConfiguration.displayOptions.showPrompts) {
                Util.WriteLineToConsole($"\n{this.text}\n", ConsoleColor.Cyan);
            }

            // Now add grounding 
            if (runtimeConfiguration.useGrounding) {
                var promptSize = Util.NumWordsIn(this.text);
                var maxGrounds = MAX_PROMPT_SIZE - promptSize;

                var documents = IR.GetRelatedDocuments($"{irKey}", runtimeConfiguration.displayOptions.showGrounding);
                var grounding = "";
                foreach (var document in documents) {
                    grounding += $"{document}{Environment.NewLine}";
                }
                grounding = Util.LimitWordCountTo(grounding, maxGrounds);
                text = $"{grounding}{Environment.NewLine}{this.text}";
            }
        }   
    }
}