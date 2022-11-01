using System.Diagnostics;
namespace NaLaPla
{
    using Microsoft.Extensions.Configuration;
    using OpenAI.GPT3;
    using OpenAI.GPT3.Managers;
    using OpenAI.GPT3.ObjectModels;
    using OpenAI.GPT3.ObjectModels.ResponseModels;
    using OpenAI.GPT3.ObjectModels.RequestModels;
    using OpenAI.GPT3.Extensions;
    using OpenAI.GPT3.Interfaces;
    using Microsoft.Extensions.Configuration.EnvironmentVariables;
    using System.ComponentModel;
    using Spectre.Console;

    enum FlagType
    {
        [Description("-LOAD   \t<filename>")]
        LOAD,           // Load plan rather than generate

        [Description("-INDEX   \t<index directory>")]
        INDEX,          // Build search index

        [Description("-DEPTH    \t<int>\t\t\tSub-plan depth")]
        DEPTH,          // Overwrite depth
        [Description("-MAXGPT   \t<int>\t\t\tMaximum concurrent requests to GPT")]
        MAXGPT,         // Maximum concurrent GPT requests
        [Description("-SUBTASKS \t<string>\t\t\tRequest <string> sub-plans")]
        SUBTASKS,        // default subtasks to ask for
        [Description("-TEMP     \t<float 0-1>\t\t\tDefault temperature")]
        TEMP,            // default temperature]
        [Description("-TEMPMULT\t<float>\t\t\tTemperature multipler per level")]
        TEMPMULT,            // default temperature]        
        [Description("-SHOWGROUND\t<bool>\t\t\tShow grounding info")]
        SHOWGROUND,            // default temperature]
        [Description("-USEGROUND\t<bool>\t\t\tEnable/disable grounding")]
        USEGROUND,            // default temperature]  
        [Description("-SHOWPROMPT\t<bool>\t\t\tShow prompts")]
        SHOWPROMPT,            // default temperature]                                
    }

    class Program {

        static Plan ?basePlan;
        static int GPTRequestsTotal = 0;

        static RuntimeConfig runtimeConfiguration = new RuntimeConfig(); // For now just has hardcoded defaults
        static SemaphoreSlim GPTSemaphore = new SemaphoreSlim(runtimeConfiguration.maxConcurrentGPTRequests,runtimeConfiguration.maxConcurrentGPTRequests);

        static List<string> PostProcessingPrompts = new List<string>() {
            "Revise the task list below removing any steps that are equivalent\n"
        };

        static async System.Threading.Tasks.Task Main(string[] args)
        {
            var root = Directory.GetCurrentDirectory();
            var dotenv = Path.Combine(root, ".env");
            DotEnv.Load(dotenv);

            var config =
                new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();

            bool bail = false;
            while (bail == false) {
                foreach (FlagType flag in Enum.GetValues(typeof(FlagType))) {
                    Console.WriteLine(Util.GetDescription(flag));
                }
                Util.WriteLineToConsole($"{runtimeConfiguration.ToString()}", ConsoleColor.Green);

                Console.WriteLine("What do you want to plan?");
                var userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) {
                    bail = true;
                } else {
                    (string planDescription, List<FlagType> flags) = ParseUserInput(userInput);

                    if (runtimeConfiguration.shouldLoadPlan) {
                        Console.WriteLine($"Loading {planDescription}");
                        basePlan = Util.LoadPlan(planDescription); 
                        bail = true;
                    }

                    if (runtimeConfiguration.indexToBuild != "") {
                        try {
                            IR.CreateIndex(new MineCraftDataProvider(runtimeConfiguration.indexToBuild));
                        }
                        catch (Exception e) {
                            Util.WriteLineToConsole("Failed to create Index", ConsoleColor.Red);
                            Util.WriteLineToConsole(e.Message.ToString(), ConsoleColor.DarkRed);
                        }
                    }

                    if (!String.IsNullOrEmpty(planDescription)) {
                        basePlan = new Plan(planDescription, null);
                        bail = true;
                    }
                }
            }
            if (basePlan is null) return; // no plan was loaded and text was blank

            var runTimer = new System.Diagnostics.Stopwatch();

            runTimer.Start();
            await ExpandPlan(basePlan); 
            runTimer.Stop();

            var runData = $"Runtime: {runTimer.Elapsed.ToString(@"m\:ss")}, GPT requests: {GPTRequestsTotal}\n";

            // Output plan
            Util.PrintPlanToConsole(basePlan, runtimeConfiguration, runData);
            var textFileName = Util.SavePlanAsText(basePlan, runtimeConfiguration, runData);
            Util.SavePlanAsJSON(basePlan);

            // Do post processing steps
            var planString = Util.PlanToString(basePlan);
            for (int i=0;i<PostProcessingPrompts.Count;i++) {
                var postPromptToUse = PostProcessingPrompts[i];
                var prompt = $"{postPromptToUse}{Environment.NewLine}START LIST{Environment.NewLine}{planString}{Environment.NewLine}END LIST";

                // Expand Max Tokens to cover size of plan
                var postPrompt = new Prompt(prompt, runtimeConfiguration);
                postPrompt.OAIConfig.MaxTokens = 2000;

                var gptResponse = await GetGPTResponses(postPrompt);

                var newFilename = Path.GetFileName(textFileName) + $"-Post{i+1}";
                var dir = Path.GetDirectoryName(textFileName);
                var ext = Path.GetExtension(textFileName);
                var saveFilename = Path.Combine(dir, newFilename + ext);

                // IDEA: Can we convert post-processed plan back into plan object?
                Util.SaveText(saveFilename, postPrompt.responses[i].ToString());
            }
        }
        
        static List<FlagType> TryGetFlag<T>(List<String>flagsAndValues, FlagType targetFlag, ref T targetSetting) {
            var flags = new List<FlagType>();
            foreach(string flagAndValue in flagsAndValues) {
                var flagStrings = flagAndValue.Split(" ");
                var flagName = flagStrings[0];
                var flagArg = flagStrings.Count() > 1 ? flagStrings[1] : null;
                FlagType flag;
                if (Enum.TryParse<FlagType>(flagName, true, out flag)) {
                    if (flag == targetFlag) {
                        if (flagArg is not null) {
                            T value = (T)Convert.ChangeType(flagArg, typeof(T));
                            if (value is not null) {
                                targetSetting = value;
                            }
                        } else {
                            targetSetting = default(T);
                        }
                        flags.Add(flag);
                    }
                }
            }
            return flags;
        }

        static (string, List<FlagType>) ParseUserInput(string userInput) {

            var pieces = userInput.Split("-").ToList();
            var planDescription = pieces[0].TrimEnd();
            pieces.RemoveAt(0);

            var flags = new List<FlagType>();

            // Settings
            flags.AddRange(TryGetFlag(pieces, FlagType.DEPTH, ref runtimeConfiguration.expandDepth));
            flags.AddRange(TryGetFlag(pieces, FlagType.TEMP, ref runtimeConfiguration.temperature));
            flags.AddRange(TryGetFlag(pieces, FlagType.TEMPMULT, ref runtimeConfiguration.tempMultPerLevel));
            flags.AddRange(TryGetFlag(pieces, FlagType.MAXGPT, ref runtimeConfiguration.maxConcurrentGPTRequests));
            flags.AddRange(TryGetFlag(pieces, FlagType.SUBTASKS, ref runtimeConfiguration.promptSubtaskCount));
            flags.AddRange(TryGetFlag(pieces, FlagType.USEGROUND, ref runtimeConfiguration.useGrounding));   
            flags.AddRange(TryGetFlag(pieces, FlagType.SHOWGROUND, ref runtimeConfiguration.displayOptions.showGrounding));                                     
            flags.AddRange(TryGetFlag(pieces, FlagType.SHOWPROMPT, ref runtimeConfiguration.displayOptions.showPrompts));                    

            // Actions
            flags.AddRange(TryGetFlag(pieces, FlagType.LOAD, ref runtimeConfiguration.shouldLoadPlan));
            flags.AddRange(TryGetFlag(pieces, FlagType.INDEX, ref runtimeConfiguration.indexToBuild));
            return (planDescription, flags);
        }


        static async System.Threading.Tasks.Task ExpandPlan(Plan planToExpand) 
        {
            if (basePlan is null) {
                throw new Exception("Got null basePlan");
            }
                                    // If I've reach max expand depth I'm done
            if (planToExpand.planLevel > runtimeConfiguration.expandDepth) {
                planToExpand.state = PlanState.FINAL;
                return;
            }

            // Always have to expand one by one if no sub-plans
            if (planToExpand.subPlans.Count() == 0 ) {
                await ExpandPlanOneByOne(planToExpand);     
            }
            else if (runtimeConfiguration.ExpandMode == ExpandModeType.ONE_BY_ONE) {
                await ExpandPlanOneByOne(planToExpand);     
            }
            else if (runtimeConfiguration.ExpandMode == ExpandModeType.AS_A_LIST) {
                await ExpandPlanAsAList(planToExpand);
            }
        }

        static async System.Threading.Tasks.Task ExpandPlanOneByOne(Plan planToExpand) 
        {
            planToExpand.state = PlanState.GPT_PROMPT_SUBMITTED;
            Util.DisplayProgress(basePlan, runtimeConfiguration, GPTSemaphore);

            // Add candidate expansions for this plan
            await AddCandidateTaskLists(planToExpand);

            // Pick best task list
            TaskList bestTaskList = await GetBestTaskList(planToExpand);

            // Create sub plans
            AddSubPlans(planToExpand, bestTaskList);

            // If expanding as a list, expand again on current plan now that it has sub-plans
            if (runtimeConfiguration.ExpandMode == ExpandModeType.AS_A_LIST) {
                await ExpandPlan(planToExpand);
            }

            // Else If expanding one by one and doing concurrent requests
            else if (runtimeConfiguration.ExpandMode == ExpandModeType.ONE_BY_ONE 
                && runtimeConfiguration.maxConcurrentGPTRequests > 1) {
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
            Util.DisplayProgress(basePlan, runtimeConfiguration, GPTSemaphore);
        }

        static async System.Threading.Tasks.Task ExpandPlanAsAList(Plan planToExpand) 
        {
            // Add candidate TaskLists to all sub-plans
            await AddCandidateTaskListsToSubPlans(planToExpand);

            foreach (var subPlan in planToExpand.subPlans) {
                // Pick bask task list
                TaskList bestTaskList = await GetBestTaskList(subPlan);

                // Create sub-plans
                AddSubPlans(subPlan, bestTaskList);
                
                if (subPlan.subPlans.Count > 0) {
                    await ExpandPlan(subPlan);
                }
            }

            planToExpand.state = PlanState.DONE;
            Util.DisplayProgress(basePlan, runtimeConfiguration, GPTSemaphore);
        }
/*TODO delete me
        static async System.Threading.Tasks.Task ExpandPlanOld(Plan planToExpand) {
            if (basePlan is null) {
                throw new Exception("Got null basePlan");
            }
            if (planToExpand.planLevel > runtimeConfiguration.expandDepth) {
                planToExpand.state = PlanState.FINAL;
                return;
            }
            planToExpand.state = PlanState.GPT_PROMPT_SUBMITTED;
            Util.DisplayProgress(basePlan, runtimeConfiguration, GPTSemaphore);
            await AddCandidateTaskLists(planToExpand);

            TaskList bestResponse;
            if (planToExpand.candidateSubTasks.Count > 0) {
                bestResponse = await GetBestTaskList(planToExpand, planToExpand.candidateSubTasks);
            } else {
                bestResponse = planToExpand.candidateSubTasks.First();
            }

            // If no best response, stop expansion
            if (bestResponse == null) {
                planToExpand.state = PlanState.DONE;
                Util.DisplayProgress(basePlan, runtimeConfiguration, GPTSemaphore);
                return;
            }

            planToExpand.state = PlanState.PROCESSING;
            // If one sub item at a time, create children and then expand with
            if (runtimeConfiguration.ExpandMode == ExpandModeType.ONE_BY_ONE) {
                
                planToExpand.subPlanDescriptions = Util.ParseSubPlanList(bestResponse.ToString());

                // Create sub-plans
                foreach (var subPlanDescription in planToExpand.subPlanDescriptions) {
                    var plan = new Plan(subPlanDescription, planToExpand);
                    planToExpand.subPlans.Add(plan);
                }
                if (runtimeConfiguration.maxConcurrentGPTRequests > 1) {
                    var plans = planToExpand.subPlans.Select(async subPlan =>
                    {
                            await ExpandPlanOld(subPlan);
                    });
                    await System.Threading.Tasks.Task.WhenAll(plans);
                } else {
                    foreach (var subPlan in planToExpand.subPlans) {
                        await ExpandPlanOld(subPlan);
                    }
                }
            }
            // Otherwise, expand all at once and then create children
            else if (runtimeConfiguration.ExpandMode == ExpandModeType.AS_A_LIST) {

                if (planToExpand.subPlanDescriptions.Count == 0) {
                    planToExpand.subPlanDescriptions = Util.ParseSubPlanList(bestResponse.ToString());
                    await ExpandPlanOld(planToExpand);
                }
                else {
                    UpdatePlan(planToExpand, bestResponse.ToString());

                    // If I haven't reached the end of the plan
                    if (planToExpand.subPlans.Count > 0 ) {
                        if (runtimeConfiguration.maxConcurrentGPTRequests > 1) {
                            var plans = planToExpand.subPlans.Select(async subPlan =>
                            {
                                if (subPlan.subPlanDescriptions.Any()) {
                                    await ExpandPlanOld(subPlan);
                                }
                            });
                            await System.Threading.Tasks.Task.WhenAll(plans);
                        } else {
                            foreach (var subPlan in planToExpand.subPlans) {
                                if (subPlan.subPlanDescriptions.Any()) {
                                    await ExpandPlanOld(subPlan);
                                }
                            }
                        }
                    }
                }
            }
            planToExpand.state = PlanState.DONE;
            Util.DisplayProgress(basePlan, runtimeConfiguration, GPTSemaphore);
        }
*/
        private static (string, List<string>) ParseListItem(string listItem) {
            var steps = listItem.Replace("\n-", " -").Split(" -").ToList().Select(s => s.TrimStart().TrimEnd(' ', '\r', '\n')).ToList();
            var description = steps[0];
            steps.RemoveAt(0);
            var subPlanDescriptions = steps;
            return (description, subPlanDescriptions);
        }

        private static void AddSubPlans(Plan planToExpand, TaskList taskList) {

            // If no best response, stop expansion
            if (taskList == null) {
                planToExpand.state = PlanState.DONE;
                Util.DisplayProgress(basePlan, runtimeConfiguration, GPTSemaphore);
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
        static async Task<TaskList> GetBestTaskList(Plan plan) {
            if (basePlan is null) {
                throw new Exception("Got null basePlan");
            }
            
            // Convert to set of unique responses (remove duplicates)
            plan.candidateSubTasks = plan.candidateSubTasks.Distinct().ToList();

            // Add cached plans
            var cachedSubTasks = PlanCache.GetTaskLists(plan.description);
            if (cachedSubTasks.Any()) {
                plan.candidateSubTasks.AddRange(cachedSubTasks);
            }

            plan.candidateSubTasks = TaskList.RemoveDuplicates(plan.candidateSubTasks);

            // If only one plan left, remove it
            if (plan.candidateSubTasks.Count == 1) {
                return plan.candidateSubTasks.First();
            }

            if (runtimeConfiguration.bestResponseChooser == BestResponseChooserType.USER) {
                var bestTaskList = GetBestResponseDeterminedByUser(plan);
                return bestTaskList;
            }
            else {  // (runtimeConfiguration.bestResponseChooser == BestResponseChooserType.GPT)
                return await GetBestResponseDeterminedByGPT(plan);
            }
        }

        // When multiple GPT responses provided, asks user which response is the best to choose between them
        static TaskList GetBestResponseDeterminedByUser(Plan plan) 
        {
            // Draw table options
            string[] optionColors  = {"blue", "lime", "Yellow", "Fuchsia", "aqua"};
            var table = new Spectre.Console.Table();
            var rows = new string[plan.candidateSubTasks.Count+1];

            table.AddColumn("Task");
            
            var expandedPlan = Util.ReversePlanToString(plan);
            Util.WriteToConsole(expandedPlan, System.ConsoleColor.White);

            expandedPlan+= Util.IndentText("  [red]<WHICH OPTION IS BEST HERE>[/]\n", plan.planLevel + 1);
            
            expandedPlan += Util.RemainingPlanToString(plan);
            rows[0] = expandedPlan;

            foreach (var data in plan.candidateSubTasks.Select((taskList, index) => (taskList, index)))
            {
                var cached = (data.taskList.fromCache) ? $"(C{data.taskList.bestCount})" : "";
                table.AddColumn($"[{optionColors[data.index]}]Option {data.index+1} {cached}[/]");
                rows[data.index+1] = $"[{optionColors[data.index]}]{data.taskList.ToString().Trim()}[/]";
            }
            table.AddRow(rows);
            AnsiConsole.Write(table);

            var userInput = Console.ReadLine();

            int index = 0;
            if (!int.TryParse(userInput, out index) || index < 1 || index > plan.candidateSubTasks.Count) {
                // Invalid prompt means don't expand,
                return null;  //TODO - return empty list instead
            }

            var bestCandidateTasks = plan.candidateSubTasks.ElementAt(index-1);

            // Add to task cache
            PlanCache.AddTaskList(plan.description, bestCandidateTasks);

            return bestCandidateTasks;
        }

        // When multiple task lists provided, asks GPT which response is the best to choose between them
        static async Task<TaskList> GetBestResponseDeterminedByGPT(Plan plan) {

            var prompt = $"Which plan is a better for a computer program to ${basePlan.description}?/n";

            foreach (var data in plan.candidateSubTasks.Select((response, index) => (response, index)))
            {
                prompt += $"START PLAN {data.index +1}\n{data.response.ToString().Trim()}\nEND PLAN {data.index +1}\n";
            }
            //Console.WriteLine(prompt);

            var bestPrompt = new Prompt(prompt, runtimeConfiguration);

            // Change: the actual responses will be in bestPrompt.responses
            var resultCount = await GetGPTResponses(bestPrompt);

            // Use voting mechanism
            int[] votes = new int[plan.candidateSubTasks.Count];

            for (int i=0;i<plan.candidateSubTasks.Count;i++) {
                foreach (var r in bestPrompt.responses) {
                    if (r.ToString().ToUpper().Contains($"PLAN {i+1}")) {
                        votes[i]++;
                    }
                }
            }

            // Get response with highest score
            var bestIndex = Array.IndexOf(votes, votes.Max());

            var bestTaskList = plan.candidateSubTasks.ElementAt(bestIndex);
            // Util.WriteToConsole($"Winner #{index+1}, score = {bestResponse.score}, r = {bestResponse.ToString()}", ConsoleColor.Red);

            return bestTaskList;
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
            plan.candidateSubTasks = candidateTaskLists;
        }

        // Given a plan adds candidate task list to each of its subplans (used when when expanding plans as a list)
        static async Task AddCandidateTaskListsToSubPlans(Plan plan) {

            var responses = await ExpandWithGPT(plan, PromptType.TASKLIST);

            foreach (string response in responses) {
                // Assume response is of the form: "1. task1 -subtask1 -subtask2 2. task2 -subtask 1..."
                // Each number corresponds to an existing sub-plan
                // Each sub-bullet, corresponds to suggested tasks for taht sub plan
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

            //TOOD - remove duplicates
        }



        static async Task<List<string>> ExpandWithGPT(Plan plan, PromptType promptType) {

            var prompt = new ExpandPrompt(basePlan, plan, promptType, 
                runtimeConfiguration.useGrounding, 
                runtimeConfiguration.promptSubtaskCount, 
                runtimeConfiguration.displayOptions.showGrounding);

            // Scale the temperature based on plan level
            var levelMult = ((float)Math.Pow(runtimeConfiguration.tempMultPerLevel, plan.planLevel));
            prompt.OAIConfig.Temperature = Math.Clamp(prompt.OAIConfig.Temperature * levelMult, 0, 1.0f);

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

            try {
                await GPTSemaphore.WaitAsync();
                CompletionCreateResponse result = await api.Completions.CreateCompletion(completionRequest, "text-davinci-002");
                if (result.Successful) {
                    var rawPlans = result.Choices.Select(c => c.Text).ToList();
                    foreach (var plan in rawPlans) {
                        prompt.responses.Add(Util.CleanListString(plan));
                    }
                    GPTRequestsTotal++;
                    return rawPlans.Count;
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