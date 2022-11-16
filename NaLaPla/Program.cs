namespace NaLaPla
{
    using Microsoft.Extensions.Configuration;
    using OpenAI.GPT3;
    using OpenAI.GPT3.Managers;
    using OpenAI.GPT3.ObjectModels.ResponseModels;
    using OpenAI.GPT3.ObjectModels.RequestModels;
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

        static int GPTRequestsTotal = 0;

        static SemaphoreSlim GPTSemaphore = new SemaphoreSlim(RuntimeConfig.settings.maxConcurrentGPTRequests,RuntimeConfig.settings.maxConcurrentGPTRequests);

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

            Plan? rootPlan = null;
            var bail = false;
            while (bail == false) {
                foreach (FlagType flag in Enum.GetValues(typeof(FlagType))) {
                    Console.WriteLine(Util.EnumToDescription(flag));
                }
                Util.WriteLineToConsole($"{RuntimeConfig.settings.ToString()}", ConsoleColor.Green);

                Console.WriteLine("What do you want to plan?");
                var userInput = Util.GetUserInput();

                if (String.IsNullOrEmpty(userInput)) {
                    bail = true;
                } else {
                    (string planDescription, List<FlagType> flags) = ParseUserInput(userInput);

                    if (RuntimeConfig.settings.shouldLoadPlan) {
                        Console.WriteLine($"Loading {planDescription}");
                        rootPlan = Util.LoadPlan(planDescription); 
                        bail = true;
                    }

                    if (RuntimeConfig.settings.indexToBuild != "") {
                        try {
                            IR.CreateIndex(new MineCraftDataProvider(RuntimeConfig.settings.indexToBuild));
                        }
                        catch (Exception e) {
                            Util.WriteLineToConsole("Failed to create Index", ConsoleColor.Red);
                            Util.WriteLineToConsole(e.Message.ToString(), ConsoleColor.DarkRed);
                        }
                    }

                    if (!String.IsNullOrEmpty(planDescription)) {
                        rootPlan = new Plan(planDescription, null);
                        bail = true;
                    }
                }
            }
            if (rootPlan == null) {
                return; // no plan was loaded and text was blank
            }

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
                var ext = Path.GetExtension(textFileName);
                var saveFilename = Path.Combine(dir, newFilename + ext);

                // IDEA: Can we convert post-processed plan back into plan object?
                Util.SaveText(saveFilename, postPrompt.responses[i].ToString());
            }
        }
        
        static private void OutputFinalPlan(Plan plan, string runData = "") {
            Util.PrintPlanToConsole(plan);
            Util.SavePlanAsJSON(plan);
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
            flags.AddRange(TryGetFlag(pieces, FlagType.DEPTH, ref RuntimeConfig.settings.expandDepth));
            flags.AddRange(TryGetFlag(pieces, FlagType.TEMP, ref RuntimeConfig.settings.temperature));
            flags.AddRange(TryGetFlag(pieces, FlagType.TEMPMULT, ref RuntimeConfig.settings.temperatureDecay));
            flags.AddRange(TryGetFlag(pieces, FlagType.MAXGPT, ref RuntimeConfig.settings.maxConcurrentGPTRequests));
            flags.AddRange(TryGetFlag(pieces, FlagType.SUBTASKS, ref RuntimeConfig.settings.promptSubtaskCount));
            flags.AddRange(TryGetFlag(pieces, FlagType.USEGROUND, ref RuntimeConfig.settings.useGrounding));   
            flags.AddRange(TryGetFlag(pieces, FlagType.SHOWGROUND, ref RuntimeConfig.settings.displayOptions.showGrounding));                                     
            flags.AddRange(TryGetFlag(pieces, FlagType.SHOWPROMPT, ref RuntimeConfig.settings.displayOptions.showPrompts));                    

            // Actions
            flags.AddRange(TryGetFlag(pieces, FlagType.LOAD, ref RuntimeConfig.settings.shouldLoadPlan));
            flags.AddRange(TryGetFlag(pieces, FlagType.INDEX, ref RuntimeConfig.settings.indexToBuild));
            return (planDescription, flags);
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
            else if (RuntimeConfig.settings.ExpandMode == ExpandModeType.ONE_BY_ONE) {
                await ExpandPlanOneByOne(plan);     
            }
            else if (RuntimeConfig.settings.ExpandMode == ExpandModeType.AS_A_LIST) {
                await ExpandPlanAsAList(plan);
            }
        }

        static async System.Threading.Tasks.Task ExpandPlanOneByOne(Plan planToExpand) 
        {
            // Add cached plans
            if (RuntimeConfig.settings.useCachedPlans)
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
            if (RuntimeConfig.settings.ExpandMode == ExpandModeType.AS_A_LIST) {
                await ExpandPlan(planToExpand);
            }

            // Else If expanding one by one and doing concurrent requests
            else if (RuntimeConfig.settings.ExpandMode == ExpandModeType.ONE_BY_ONE 
                && RuntimeConfig.settings.maxConcurrentGPTRequests > 1) {
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
            if (RuntimeConfig.settings.useCachedPlans) {
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

            if (RuntimeConfig.settings.bestResponseChooser == BestResponseChooserType.USER) {
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
                var userInput = Util.GetUserInput("Which OPTION is best? (? for help)");

                // If user want to to quit, terminate the program and print the plan
                if (userInput == "q") {
                    Util.WriteLineToConsole("Quitting...", ConsoleColor.Red);
                    OutputFinalPlan(plan.root);
                    Environment.Exit(0);
                }
                else if (userInput == "p") {
                    BestTaskListExamples.CreateSamplePlan(plan);
                }
                else if (userInput == "r") {
                    Util.WriteLineToConsole(plan.Reasoning(), ConsoleColor.Cyan);
                }
                else if (userInput == "?") {
                    Util.WriteLineToConsole("#: selected option (0 - none)\np: create prompt example\nr: show reasoning\nq: save and quit\n", ConsoleColor.Cyan);
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
                RuntimeConfig.settings.useGrounding, 
                RuntimeConfig.settings.promptSubtaskCount, 
                RuntimeConfig.settings.displayOptions.showGrounding);

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

            if (RuntimeConfig.settings.displayOptions.showPrompts) {
                Util.WriteLineToConsole("---- PROMPT ----", ConsoleColor.DarkYellow);
                Util.WriteLineToConsole(prompt.text, ConsoleColor.Yellow);
            }

            try {
                await GPTSemaphore.WaitAsync();
                CompletionCreateResponse result = await api.Completions.CreateCompletion(completionRequest, "text-davinci-002");
                if (result.Successful) {
                    var resultStrings = result.Choices.Select(c => c.Text).ToList();
                    foreach (var resultString in resultStrings) {
                        prompt.responses.Add(Util.CleanListString(resultString));

                        if (RuntimeConfig.settings.displayOptions.showResults) {
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