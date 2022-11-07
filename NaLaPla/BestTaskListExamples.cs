namespace NaLaPla
{
    // An list of plans and the selected best TaskList which can be used for prompting
    // for selection of the best task list
    public class BestTaskListExamples {

        private const string SAMPLE_FILENAME = "SamplePlans";

        public const string REASONING_PROMPT = "<EXPLAIN THE REASONING FOR WHY EACH OPTION IS GOOD OR BAD>";

        private static List<SamplePlan> _samplePlans = null;

        private static List<SamplePlan> samplePlans {
            get {
                if (_samplePlans == null) {
                    _samplePlans = LoadSamplePlans();
                }
                return _samplePlans;
            }
        }

        public static void CreateSamplePlan(Plan plan) {
            var testIndex = Util.GetUserInput("Enter best Option number:");
            var bestIndex = Util.StringToIndex(testIndex, plan.candidateSubTasks.Count);

            for (int i=0;i<plan.candidateSubTasks.Count;i++) {
                var taskList = plan.candidateSubTasks.ElementAt(i);

                Util.WriteToConsole($"Specify a reason <OPTION {i+1}> is ");
                if (i == bestIndex) {
                    Util.WriteLineToConsole("GOOD", ConsoleColor.Green);    
                }
                else {
                    Util.WriteLineToConsole("BAD", ConsoleColor.Red);    
                }
                taskList.reason = Util.GetUserInput();
            }
            AddSamplePlan(plan, bestIndex);
        }

        private static void AddSamplePlan(Plan plan, int bestIndex) {

            // Find existing item
            var samplePlan = samplePlans.FirstOrDefault(p => p.plan.description == plan.description);
            if (samplePlan == null) {
                samplePlan = new SamplePlan(plan, bestIndex);
                samplePlans.Add(samplePlan);
            }
            else {
                samplePlan.bestTaskListIndex = bestIndex;
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

        static private string MakePlanPrompt(Plan plan) {
            var prompt = "";
            prompt += MakeBestResponsePrompt(plan);
            prompt += "<QUESTION>\n";
            prompt += "Select the best OPTION to insert into the plan below:\n";
            prompt += Util.MakeTaskPrompt(plan, "<WHICH OPTION IS BEST HERE>");

            prompt+= "<ANSWER>\n";
            return prompt;
        }

        static private string MakeSamplePlanPrompt(SamplePlan samplePlan) {
            var prompt = MakePlanPrompt(samplePlan.plan);

            prompt+= $"<OPTION {samplePlan.bestTaskListIndex+1}>\n";
            prompt+= samplePlan.plan.candidateSubTasks.ElementAt(samplePlan.bestTaskListIndex).ToString();
            
            // Add reasoning
            prompt += $"{REASONING_PROMPT}\n";
            for (int i=0; i<samplePlan.plan.candidateSubTasks.Count; i++) {
                var status = i == samplePlan.bestTaskListIndex ? "GOOD" : "BAD";
                prompt += $"<OPTION {i+1}> is {status} because ";
                var taskList = samplePlan.plan.candidateSubTasks[i];
                prompt += $"{taskList.reason}\n";
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
            var samplesString = Util.LoadText(SAMPLE_FILENAME);
            if (String.IsNullOrEmpty(samplesString)) {
                return new List<SamplePlan>();
            }   
            return Newtonsoft.Json.JsonConvert.DeserializeObject<List<SamplePlan>>(samplesString);
        }

        private static void SaveSamplePlans() {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(_samplePlans);
            Util.SaveText(SAMPLE_FILENAME, json);
        }
    }

    // A plans and the index of TaskList selected as the best response
    public class SamplePlan {

        public Plan plan;

        // Index of TaskList that was chosen as the best response
        public int bestTaskListIndex;

        public SamplePlan(Plan plan, int bestTaskListIndex) {
            this.plan = plan;
            this.bestTaskListIndex = bestTaskListIndex;
        }
    }
}