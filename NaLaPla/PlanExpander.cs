namespace NaLaPla
{
    using OpenAI.GPT3;
    using OpenAI.GPT3.Managers;
    using OpenAI.GPT3.ObjectModels.ResponseModels;
    using OpenAI.GPT3.ObjectModels.RequestModels;
    using Spectre.Console;

    class PlanExpander {

        static int GPTRequestsTotal = 0;

        static SemaphoreSlim GPTSemaphore = new SemaphoreSlim(RuntimeConfig.settings.gptMaxConcurrentRequests,RuntimeConfig.settings.gptMaxConcurrentRequests);

        static List<string> PostProcessingPrompts = new List<string>() {
            "Revise the task list below removing any steps that are equivalent\n"
        };

        static public async Task ExpandPlan(string planDescription)
        {
            Plan rootPlan = new Plan(planDescription, null);

            var runTimer = new System.Diagnostics.Stopwatch();

            runTimer.Start();
            await ExpandPlan(rootPlan); 
            runTimer.Stop();

            var runData = $"Runtime: {runTimer.Elapsed.ToString(@"m\:ss")}, GPT requests: {GPTRequestsTotal}\n";

            // Output plan
            OutputFinalPlan(rootPlan, runData);

            // Do post processing steps
            var planString = rootPlan.PlanToString();
            var textFileName = Util.SavePlanAsText(rootPlan, runData);
            for (int i=0;i<PostProcessingPrompts.Count;i++) {
                var postPromptToUse = PostProcessingPrompts[i];
                var prompt = $"{postPromptToUse}{Environment.NewLine}START LIST{Environment.NewLine}{planString}{Environment.NewLine}END LIST";

                // Expand Max Tokens to cover size of plan
                var openAIConfig = new OpenAIConfig(maxTokens: 2000, OpenAIConfig.DefaultNumResponses, OpenAIConfig.DefaultTemperature);
                var postPrompt = new Prompt(prompt, openAIConfig);

                var gptResponse = await GetGPTResponses(postPrompt);

                var newFilename = Path.GetFileName(textFileName) + $"-Post{i+1}";
                var dir = Path.GetDirectoryName(textFileName);
                if (dir == null) {
                    throw new Exception("Could not get directory name");
                }
                var ext = Path.GetExtension(textFileName);
                var saveFilename = Path.Combine(dir, newFilename + ext);

                // IDEA: Can we convert post-processed plan back into plan object?
                Util.SaveText(Util.OUTPUT_DIRECTORY, saveFilename, Util.TEXT_FILE_EXTENSION, postPrompt.responses[i].ToString());
            }
        }
        
        static private void OutputFinalPlan(Plan plan, string runData = "") {
            Util.PrintPlanToConsole(plan);
            Util.SavePlanAsJSON(plan);
        }

        static async System.Threading.Tasks.Task ExpandPlan(Plan plan) 
        {
            // If I've reach max expand depth I'm done
            if (plan.planLevel > RuntimeConfig.settings.expandDepth) {
                plan.state = PlanState.FINAL;
                return;
            }

            Util.DisplayProgress(plan.root, GPTSemaphore);

            // Always have to expand one by one if no sub-plans
            if (plan.subPlans.Count() == 0 ) {
                await ExpandPlanOneByOne(plan);     
            }
            else if (RuntimeConfig.settings.expandMode == ExpandModeType.ONE_BY_ONE) {
                await ExpandPlanOneByOne(plan);     
            }
            else if (RuntimeConfig.settings.expandMode == ExpandModeType.AS_A_LIST) {
                await ExpandPlanAsAList(plan);
            }
        }

        static async System.Threading.Tasks.Task ExpandPlanOneByOne(Plan planToExpand) 
        {
            // Add cached plans
            if (RuntimeConfig.settings.expandUseCachedPlans)
            {
                var cachedSubTasks = PlanCache.GetTaskLists(planToExpand.description);
                if (cachedSubTasks.Any()) {
                    planToExpand.candidateSubTasks.AddRange(cachedSubTasks);
                }
            }

            planToExpand.state = PlanState.GPT_PROMPT_SUBMITTED;

            // Add candidate expansions for this plan
            await AddCandidateTaskLists(planToExpand);

            // Pick best task list
            TaskList? bestTaskList = await GetBestTaskList(planToExpand);

            // Create sub plans
            AddSubPlans(planToExpand, bestTaskList);

            // If expanding as a list, expand again on current plan now that it has sub-plans
            if (RuntimeConfig.settings.expandMode == ExpandModeType.AS_A_LIST) {
                await ExpandPlan(planToExpand);
            }

            // Else If expanding one by one and doing concurrent requests
            else if (RuntimeConfig.settings.expandMode == ExpandModeType.ONE_BY_ONE 
                && RuntimeConfig.settings.gptMaxConcurrentRequests > 1) {
                var plans = planToExpand.subPlans.Select(async subPlan =>
                {
                        await ExpandPlan(subPlan);
                });
                await System.Threading.Tasks.Task.WhenAll(plans);
            } 
            // Otherwise expand one at a time
            else {
                foreach (var subPlan in planToExpand.subPlans) {
                    await ExpandPlan(subPlan);
                }
            }

            planToExpand.state = PlanState.DONE;
        }

        static async Task ExpandPlanAsAList(Plan planToExpand) 
        {
            // Add cached plans
            if (RuntimeConfig.settings.expandUseCachedPlans) {
                foreach (Plan subPlan in planToExpand.subPlans) { 
                    var cachedSubTasks = PlanCache.GetTaskLists(subPlan.description);
                    if (cachedSubTasks.Any()) {
                        subPlan.candidateSubTasks.AddRange(cachedSubTasks);
                    }
                }
            }

            // Add candidate TaskLists to all sub-plans
            await AddCandidateTaskListsToSubPlans(planToExpand);

            foreach (var subPlan in planToExpand.subPlans) {

                // If plan doesn't have dotNotExpand option, add it or move to first position
                subPlan.AddDoNotExpandOption();

                // Pick bask task list
                TaskList? bestTaskList = await GetBestTaskList(subPlan);

                // Create sub-plans
                AddSubPlans(subPlan, bestTaskList);
                
                if (subPlan.subPlans.Count > 0) {
                    await ExpandPlan(subPlan);
                }
            }

            planToExpand.state = PlanState.DONE;
        }

        private static (string, List<string>) ParseListItem(string listItem) {
            var steps = listItem.Replace("\n-", " -").Split(" -").ToList().Select(s => s.TrimStart().TrimEnd(' ', '\r', '\n')).ToList();
            var description = steps[0];
            steps.RemoveAt(0);
            var subPlanDescriptions = steps;
            return (description, subPlanDescriptions);
        }

        private static void AddSubPlans(Plan planToExpand, TaskList? taskList) {

            // If no best response, stop expansion
            if (taskList == null || taskList.taskDescriptions.Count == 0) {
                planToExpand.state = PlanState.DONE;
                return;
            }
            planToExpand.state = PlanState.PROCESSING;

            // Create sub-plans
            foreach (var taskDescription in taskList.taskDescriptions) {
                var plan = new Plan(taskDescription, planToExpand);
                planToExpand.subPlans.Add(plan);
            }
        }

        // When multiple GPT responses provided, chooses between them
        static async Task<TaskList?> GetBestTaskList(Plan plan) {

            if (plan.candidateSubTasks.Count == 0) {
                return null;
            }

            plan.candidateSubTasks = TaskList.RemoveDuplicates(plan.candidateSubTasks);

            if (RuntimeConfig.settings.expandResponseChooser == ResponseChooserType.USER) {
                var bestTaskList = await GetBestResponseDeterminedByUser(plan);
                return bestTaskList;
            }
            else {  // (RuntimeConfig.settings.bestResponseChooser == BestResponseChooserType.GPT)
                var bestIndex = await GetBestResponseDeterminedByGPT(plan);
                return plan.candidateSubTasks[bestIndex];
            }
        }

        // When multiple GPT responses provided, asks user which response is the best to choose between them
        static async Task<TaskList?> GetBestResponseDeterminedByUser(Plan plan) 
        {
            var gptBestIndex = await GetBestResponseDeterminedByGPT(plan);

            // Draw table options
            string[] optionColors  = {"blue", "lime", "Yellow", "Fuchsia", "aqua", "cyan1", "blueviolet", "chartreuse2_1"};
            var table = new Spectre.Console.Table();

            var taskRow = new string[plan.candidateSubTasks.Count+1];
            var reasonRow = new string[plan.candidateSubTasks.Count+1];

            table.AddColumn("Task");
            var taskPrompt = Util.MakeTaskPrompt(plan, $"[red]{BestTaskListExamples.WHICH_PROMPT}[/]");
            taskRow[0] = taskPrompt;
            reasonRow[0] = "[red]GPT Reason[/]";

            foreach (var data in plan.candidateSubTasks.Select((taskList, index) => (taskList, index)))
            {
                var optionColor = optionColors[data.index % optionColors.Length];
                var gtpSelected = data.index == gptBestIndex ? "[red](GPT)[/]" : "";
                var cached = (data.taskList.fromCache) ? $"(C{data.taskList.bestCount})" : "";
                table.AddColumn($"[{optionColor}]Option {data.index+1} {cached}[/] {gtpSelected}");
                taskRow[data.index+1] = $"[{optionColor}]{data.taskList.ToString().Trim()}[/]";

                var reasonColor = data.index == gptBestIndex  ? "green" : "red";
                reasonRow[data.index+1] = $"[{reasonColor}]{data.taskList.reason}[/]";
            }
            table.AddRow(taskRow);
            table.HorizontalBorder();
            table.AddRow(reasonRow);
            AnsiConsole.Write(table);

            var selectedIndex = UsersSelectedTaskList(plan);
            if (selectedIndex < 0) {
                return null;
            }

            var bestCandidateTasks = plan.candidateSubTasks.ElementAt(selectedIndex);

            // If the user chose dontExpand, increment the cache count and return null
            if (bestCandidateTasks.doNotExpand) {
                PlanCache.AddNoExpandTaskList(plan.description);
                return null;
            }

            // Add to task cache
            PlanCache.AddTaskList(plan.description, bestCandidateTasks);

            return bestCandidateTasks;
        }

        static private int UsersSelectedTaskList(Plan plan) {

            while (true) {
                var userInput = Util.GetUserInput("Which OPTION is best? ('?' for help)").ToUpper();

                // If user want to to quit, terminate the program and print the plan
                if (userInput == "Q") {
                    Util.WriteLineToConsole("Quitting...", ConsoleColor.Red);
                    OutputFinalPlan(plan.root != null ? plan.root : plan);
                    Environment.Exit(0);
                }
                else if (userInput == "P") {
                    BestTaskListExamples.CreateSamplePlan(plan);
                }
                else if (userInput == "R") {
                    Util.WriteLineToConsole(plan.Reasoning(), ConsoleColor.Cyan);
                }
                else if (userInput == "S") {
                    CommandLineInterface.UpdateSettings();
                }
                else if (userInput == "?") {
                    Util.WriteLineToConsole("#) Selected option (0 - none)\nP) create prompt example\nR) show reasoning\nS) Settings\nQ) save and quit\n", ConsoleColor.Cyan);
                }
                else {  
                    return Util.StringToIndex(userInput, plan.candidateSubTasks.Count);             
                }
            }
        }

        // When multiple task lists provided, asks GPT which response is the best to choose between them
        static async Task<int> GetBestResponseDeterminedByGPT(Plan plan) {

            var promptString = BestTaskListExamples.TaskListPrompt(plan);

            // Use zero temperature to get a single best response
            var openAIConfig = new OpenAIConfig(OpenAIConfig.DefaultMaxTokens, numResponses: 1, temperature: 0);
            var prompt = new Prompt(promptString, openAIConfig);

            // Change: the actual responses will be in bestPrompt.responses
            var resultCount = await GetGPTResponses(prompt);

            Util.ParseReasoningResponse(prompt.responses[0], plan.candidateSubTasks);

            return plan.BestTaskListIndex();
        }

        // Given a Plan, adds candidate task lists for that plan (used when expanding one plan at a time)
        static async Task AddCandidateTaskLists(Plan plan) {

            var promptType = (plan.planLevel == 0) ? PromptType.FIRSTPLAN : PromptType.TASK;
            var responses = await ExpandWithGPT(plan, promptType);

            // Now generate candidate task lists from teach response
            var candidateTaskLists = new List<TaskList>();
            foreach (var response in responses) {
                candidateTaskLists.Add(new TaskList(response));
            }
            plan.candidateSubTasks.AddRange(candidateTaskLists);
        }

        // Given a plan adds candidate task list to each of its sub-plans (used when when expanding plans as a list)
        static async Task AddCandidateTaskListsToSubPlans(Plan plan) {

            var responses = await ExpandWithGPT(plan, PromptType.TASKLIST);

            foreach (string response in responses) {
                // Assume response is of the form: "1. task1 -subtask1 -subtask2 2. task2 -subtask 1..."
                // Each number corresponds to an existing sub-plan
                // Each sub-bullet, corresponds to suggested tasks for that sub plan
                var bulletedItem = Util.NumberToBullet(response);
                var list = Util.ParseListToLines(bulletedItem).ToList();

                // Break is list item into parent and sub-items
                foreach (var listItem in list) {
                    // Split line in to plan description and sub-task descriptions
                    (string planDescription, List<string> subPlanDescriptions) = ParseListItem(listItem);

                    if (subPlanDescriptions.Count >= 0) {
                        // Find the subplan
                        var subPlan = plan.subPlans.FirstOrDefault(p => p.description == planDescription);
                        if (subPlan != null) {
                            subPlan.candidateSubTasks.Add(new TaskList(subPlanDescriptions));
                        }
                    }
                }
            }

            //TODO - remove duplicates
        }



        static async Task<List<string>> ExpandWithGPT(Plan plan, PromptType promptType) {

            var prompt = new ExpandPrompt(plan, promptType, 
                RuntimeConfig.settings.promptUseGrounding, 
                RuntimeConfig.settings.promptSubtaskCount, 
                RuntimeConfig.settings.showGrounding);

            // Scale the temperature based on plan level
            // Experiment: Disable for now
            //var levelMult = ((float)Math.Pow(RuntimeConfig.settings.tempMultPerLevel, plan.planLevel));
            //prompt.OAIConfig.Temperature = Math.Clamp(prompt.OAIConfig.Temperature * levelMult, 0, 1.0f);

            // TODO: this should just return the responses
            var gptResponseCount = await GetGPTResponses(prompt);

            return prompt.responses;
        }

        // Return list of candidate plans
        static async Task<int> GetGPTResponses(Prompt prompt) {
            var apiKey = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (apiKey is null) {
                throw new Exception("Please specify api key in .env");
            }

            var api = new OpenAIService(new OpenAiOptions()
            {
                ApiKey =  apiKey
            });

            var completionRequest = new CompletionCreateRequest();
            completionRequest.Prompt = prompt.text;
            completionRequest.MaxTokens = prompt.OAIConfig.MaxTokens;
            completionRequest.Temperature = prompt.OAIConfig.Temperature;
            completionRequest.N = prompt.OAIConfig.NumResponses;

            if (RuntimeConfig.settings.showPrompts) {
                Util.WriteLineToConsole("---- PROMPT ----", ConsoleColor.DarkYellow);
                Util.WriteLineToConsole(prompt.text, ConsoleColor.Yellow);
            }

            try {
                await GPTSemaphore.WaitAsync();
                Util.WriteLineToConsole("Sending request to GPT...", ConsoleColor.DarkGray);
                CompletionCreateResponse result = await api.Completions.CreateCompletion(completionRequest, "text-davinci-002");
                if (result.Successful) {
                    var resultStrings = result.Choices.Select(c => c.Text).ToList();
                    foreach (var resultString in resultStrings) {
                        prompt.responses.Add(Util.CleanListString(resultString));

                        if (RuntimeConfig.settings.showResults) {
                            Util.WriteLineToConsole("---- RESULT ----", ConsoleColor.DarkCyan);
                            Util.WriteLineToConsole(resultString, ConsoleColor.Cyan);
                        }
                    }
                    GPTRequestsTotal++;
                    return resultStrings.Count;
                } else {
                    // TODO: Handle failures in a smarter way
                    if (result.Error is not null) {
                        Console.WriteLine($"{result.Error.Code}: OpenAI = {result.Error.Message}");
                    }
                }
                throw new Exception("API Failure");
            }

            finally {
                GPTSemaphore.Release();
            }
        }
    }
}