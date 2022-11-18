namespace NaLaPla
{
    using System.Runtime.InteropServices;
    using Newtonsoft.Json;

    // An list of plans and the selected best TaskList which can be used for prompting
    // for selection of the best task list
    public class BestTaskListExamples {

        private const string SAMPLE_FILENAME = "SamplePlans";

        public const string REASONING_PROMPT = "<EXPLAIN THE REASONING FOR WHY EACH OPTION IS GOOD OR BAD>";

        public const string WHICH_PROMPT = "<WHICH OPTION IS BEST HERE>";

        private static List<SamplePlan>? _samplePlans;

        private static List<SamplePlan> samplePlans {
            get {
                if (_samplePlans == null) {
                    _samplePlans = LoadSamplePlans();
                }
                return _samplePlans;
            }
        }


        public static void CreateSamplePlan(Plan plan) {

            Util.WriteToConsole("Specify the order of Options from best to worst (i.e. '1 3 2')");
            var orderText = Util.GetUserInput();
            var splitText = orderText.Split(" ");
            var order = splitText.Select(n => Util.StringToIndex(n, plan.candidateSubTasks.Count)).ToList();
    
            for (int rank = 0; rank < order.Count; rank++) {
                var index = order[rank];
                var taskList = plan.candidateSubTasks[index];
                taskList.ranking = rank;

                var taskListIndex = plan.candidateSubTasks.IndexOf(taskList);
                taskList.reason = Util.GetUserInput($"<Option {taskListIndex+1}> the {IndexToString(rank)}best option because:");
            }
            AddSamplePlan(plan);
        }

        private static void AddSamplePlan(Plan plan) {

            // Find existing item
            var samplePlan = samplePlans.FirstOrDefault(p => p.plan != null && p.plan.description == plan.description);
            if (samplePlan == null) {
                samplePlan = new SamplePlan(plan);
                samplePlans.Add(samplePlan);
            }

            SaveSamplePlans();
        }

        // Make prompt showing different expansion options for a task
        /*
            <OPTION 1>
            - place block for the walls
            - make sure the walls are tall enough

            <OPTION 2>
            - place blocks in the area for the walls
            - make sure the walls are the same size as the house   
        */
        static string MakeBestResponsePrompt(Plan plan) {
            var prompt = "";
            for (int i=0; i<plan.candidateSubTasks.Count; i++) {
                prompt += $"<OPTION {i+1}>\n";
                var taskList = plan.candidateSubTasks[i];
                prompt += $"{taskList.ToString()}\n";
            }
            return prompt;
        }

        static private string MakePlanPrompt(Plan plan, [Optional] SamplePlan samplePlan) {
            var prompt = "";
            prompt += MakeBestResponsePrompt(plan);
            prompt += "<QUESTION>\n";
            prompt += "Rank the OPTIONS to insert into the plan from best to worst and provide your reasoning\n";
            prompt += "<PLAN>\n";
            // Sample plans cache the plan prompts as they don't store the full plan
            if (samplePlan != null) {
                if (RuntimeConfig.settings.bestTaskPrompt == BestTaskPromptType.PARTIAL) {
                    prompt += samplePlan.partialPlanPrompt;
                }
                else {
                    prompt += samplePlan.fullPlanPrompt;
                }
            }
            // Otherwise we can generate the plan prompt from the plan
            else {
                prompt += Util.MakeTaskPrompt(plan, WHICH_PROMPT);
            }
            
            /*
            prompt += "Select the best OPTION to insert into the plan below:\n";
            prompt += Util.MakeTaskPrompt(plan, "<WHICH OPTION IS BEST HERE>");
            */  
            prompt+= "<ANSWER>\n";
            return prompt;
        }

        static private string IndexToString(int i) {
            if (i == 0) {
                return "";
            }
            else if (i == 1) {
                return "second ";
            }
            else if (i == 2) {
                return "third ";
            }
            else {
                return $"{i+1}th ";
            }
        }

        static private string MakeSamplePlanPrompt(SamplePlan samplePlan) {

            if (samplePlan.plan == null) {
                throw new Exception("Sample plan has no plan");
            }

            var prompt = MakePlanPrompt(samplePlan.plan, samplePlan);

            for (int i=0;i<samplePlan.plan.candidateSubTasks.Count; i++) {
                var taskList = samplePlan.plan.candidateSubTasks.FirstOrDefault(t => t.ranking == i);
                if (taskList == null) {
                    throw new Exception("No task list found for ranking");
                }
                var taskIndex = samplePlan.plan.candidateSubTasks.IndexOf(taskList);
                prompt += $"<OPTION {taskIndex+1}> the {IndexToString(i)}best option {taskList.reason}\n";
            }
            return prompt;
        }

        // Returns string of plans snippets and the selected best TaskList which can be used for prompting
        public static string TaskListPrompt(Plan plan) {
            var prompt = "";
            for (int i = 0; i < samplePlans.Count; i++) {
                prompt += $"<PROBLEM {i+1}>\n";
                var samplePlan = samplePlans.ElementAt(i);
                prompt += MakeSamplePlanPrompt(samplePlan) + "\n";
            }
            prompt += $"<PROBLEM {samplePlans.Count+1}>\n";
            prompt += MakePlanPrompt(plan);
            return prompt;
        }

        private static List<SamplePlan> LoadSamplePlans() {
            var samplesString = Util.LoadText("", SAMPLE_FILENAME, Util.JSON_FILE_EXTENSION);
            if (samplesString == null || String.IsNullOrEmpty(samplesString)) {
                return new List<SamplePlan>();
            }   
            var samplePlans = JsonConvert.DeserializeObject<List<SamplePlan>>(samplesString);
            if (samplePlans == null) {
                return new List<SamplePlan>();
            }
            return samplePlans;
        }

        private static void SaveSamplePlans() {
            var json = JsonConvert.SerializeObject(_samplePlans);
            Util.SaveText("", SAMPLE_FILENAME, Util.JSON_FILE_EXTENSION, json);
        }
    }
}