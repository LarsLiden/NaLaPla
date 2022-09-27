﻿namespace NaLaPla
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

    enum ExpandModeType
    {
        ONE_BY_ONE,
        AS_A_LIST    
    }

    enum FlagType
    {
        LOAD,           // Load plan rather than generate

        DEPTH,          // Overwrite depth
    }

    class Program {
    
        static Task ?basePlan;
        static int GPTRequestsTotal = 0;
        static int GPTRequestsInFlight = 0;

        static ExpandModeType ExpandMode = ExpandModeType.AS_A_LIST;
        
        static int ExpandDepth = 2;

        static OpenAIConfig OAIConfig = new OpenAIConfig();

        const string ExpandSubtaskCount = "four";
        const bool parallelGPTRequests = true;
        const bool showPrompts = false; // whether or not to print each prompt as it is submitted to GPT. Prompts always stored in plan.prompt.
        const bool showResults = false; // print the parsed result of each request to the console
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

            //Util.TestParseMultiList();
            //return;
            var configList = $"ExpandDepth = {ExpandDepth}, ExpandSubtaskCount = {ExpandSubtaskCount}, "
                + $"parallelGPTRequests = {parallelGPTRequests}, showPrompts = {showPrompts}";
            Util.WriteToConsole($"\n\n\n{configList}", ConsoleColor.Green);
            Console.WriteLine("What do you want to plan?");
            var userInput = Console.ReadLine();
            var runTimer = new System.Diagnostics.Stopwatch();
            runTimer.Start();

            (string planDescription, List<FlagType> flags) = ParseUserInput(userInput);

             if (flags.Contains(FlagType.LOAD)) {
                basePlan = Util.LoadPlan(planDescription);
            }
            else {
                basePlan = new Task() {
                    description = planDescription,
                    planLevel = 0, 
                    subTasks = new List<Task>()
                    };

                await ExpandPlan(basePlan);
                runTimer.Stop();

                var runData = $"Runtime: {runTimer.Elapsed.ToString(@"m\:ss")}, GPT requests: {GPTRequestsTotal}\n";

                // Output plan
                Util.PrintPlanToConsole(basePlan, configList, runData);
                Util.SavePlanAsText(basePlan, configList, runData);
                Util.SavePlanAsJSON(basePlan);
            }

            // Do post processing steps
            var planString = Util.PlanToString(basePlan);
            for (int i=0;i<PostProcessingPrompts.Count;i++) {
                var postPrompt = PostProcessingPrompts[i];
                var prompt = $"{postPrompt}{Environment.NewLine}START LIST{Environment.NewLine}{planString}{Environment.NewLine}END LIST";

                // Expand Max Tokens to cover size of plan
                OAIConfig.MaxTokens = 2000;

                //var gptResponse = await GetGPTResponse(prompt);

                var outputName = $"{planDescription}-Post{i+1}";

                // IDEA: Can we convert post-processed plan back into plan object?
                //Util.SaveText(outputName, gptResponse);
            }
            
        }

        static (string, List<FlagType>) ParseUserInput(string userInput) {
            var pieces = userInput.Split("-").ToList();
            var planDescription = pieces[0].TrimEnd();
            pieces.RemoveAt(0);
            var flags = new List<FlagType>();
            FlagType flag;
            foreach (string flagAndValue in pieces) {
                var flagStrings = flagAndValue.Split(" ");
                var flagName = flagStrings[0];
                var flagArg = flagStrings.Count() > 1 ? flagStrings[1] : null;
                if (Enum.TryParse<FlagType>(flagName, true, out flag)) {

                    // Should overwrite depth amount?
                    if (flag == FlagType.DEPTH) {
                        int expandDepth;
                        if (int.TryParse(flagArg, out expandDepth)) {
                            ExpandDepth = expandDepth;
                        }
                    }
                    flags.Add(flag);
                }
            }

            return (PlanDescription: planDescription, Flags: flags);
        }

        static async System.Threading.Tasks.Task ExpandPlan(Task planToExpand) {

            if (planToExpand.planLevel > ExpandDepth) {
                planToExpand.state = TaskState.FINAL;
                return;
            }
            planToExpand.state = TaskState.GPT_PROMPT_SUBMITTED;
            Util.DisplayProgress(basePlan, GPTRequestsInFlight);
            var gptResponse = await GetGPTResponse(planToExpand, showPrompts);
            planToExpand.state = TaskState.PROCESSING;
            // If one sub item at a time, create children and then expand with
            if (ExpandMode == ExpandModeType.ONE_BY_ONE) {
                
                planToExpand.subTaskDescriptions = Util.ParseSubTaskList(gptResponse);

                // Create sub-tasks
                foreach (var subTask in planToExpand.subTaskDescriptions) {
                    var subPlan = new Task() {
                        description = subTask,
                        planLevel = planToExpand.planLevel + 1,
                        subTasks = new List<Task>(),
                        parent = planToExpand
                    };
                    planToExpand.subTasks.Add(subPlan);
                }
                if (parallelGPTRequests) {
                    var tasks = planToExpand.subTasks.Select(async subTask =>
                    {
                            await ExpandPlan(subTask);
                    });
                    await System.Threading.Tasks.Task.WhenAll(tasks);
                } else {
                    foreach (var subPlan in planToExpand.subTasks) {
                        await ExpandPlan(subPlan);
                    }
                }
            }
            // Otherwise, expand all at once and then create children
            else if (ExpandMode == ExpandModeType.AS_A_LIST) {

                if (planToExpand.subTaskDescriptions.Count == 0) {
                    planToExpand.subTaskDescriptions = Util.ParseSubTaskList(gptResponse);
                    await ExpandPlan(planToExpand);
                }
                else {
                    // Only request a display if we're not using parallel requests
                    UpdatePlan(planToExpand, gptResponse, !parallelGPTRequests);

                    // If I haven't reached the end of the plan
                    if (planToExpand.subTasks.Count > 0 ) {
                        if (parallelGPTRequests) {
                            var tasks = planToExpand.subTasks.Select(async subTask =>
                            {
                                if (subTask.subTaskDescriptions.Any()) {
                                    await ExpandPlan(subTask);
                                }
                            });
                            await System.Threading.Tasks.Task.WhenAll(tasks);
                        } else {
                            foreach (var subPlan in planToExpand.subTasks) {
                                if (subPlan.subTaskDescriptions.Any()) {
                                    await ExpandPlan(subPlan);
                                }
                            }
                        }
                    }
                }
            }
            planToExpand.state = TaskState.DONE;
            Util.DisplayProgress(basePlan,GPTRequestsInFlight);
        }

        public static void UpdatePlan(Task plan, string gptResponse, bool showResults) {

            // Assume list is like: "1. task1 -subtask1 -subtask2 2. task2 -subtask 1..."
            var bulletedItem = Util.NumberToBullet(gptResponse);
            var list = Util.ParseListToLines(bulletedItem).ToList();

            // When GPT can't find any more subtasks, it just add to the end of the list
            if (list.Count() > plan.subTaskDescriptions.Count()) {
                return;
            }
            plan.subTasks = new List<Task>();
            foreach (var item in list) {
                var steps = item.Split("-").ToList().Select(s => s.TrimStart().TrimEnd(' ', '\r', '\n')).ToList();

                // Check if the plan has bottomed out
                if (steps[0]=="") {
                    plan.subTaskDescriptions = steps;
                    return;
                }
                
                var description = steps[0];
                steps.RemoveAt(0);
                var subPlan = new Task() {
                        description = description,
                        planLevel = plan.planLevel + 1, 
                        subTaskDescriptions = steps,
                        subTasks = new List<Task>()
                    };
                plan.subTasks.Add(subPlan);
            }
            if (showResults) {
                Util.PrintPlanToConsole(plan);
            }
        }

        static async Task<string> GetGPTResponse(Task plan, bool showPrompt) {
            var prompt = GeneratePrompt(plan,showPrompt);
            var GPTresponse = await GetGPTResponse(prompt);
            plan.GPTresponse = GPTresponse;
            return GPTresponse;
        }

        static async Task<string> GetGPTResponse(string prompt) {
            var apiKey = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY");

            var api = new OpenAIService(new OpenAiOptions()
            {
                ApiKey =  apiKey
            });

            var completionRequest = new CompletionCreateRequest();
            completionRequest.Prompt = prompt;
            completionRequest.MaxTokens = OAIConfig.MaxTokens;
            completionRequest.Temperature = OAIConfig.Temperature;
            completionRequest.N = OAIConfig.NumResponses;

            GPTRequestsInFlight++;
            CompletionCreateResponse result = await api.Completions.CreateCompletion(completionRequest, "text-davinci-002");
            GPTRequestsInFlight--;

            if (result.Successful)
            {

                var rawPlan = result.Choices[0].Text;
                GPTRequestsTotal++;
                return rawPlan;
            }
            else {
                // TODO: Handle failures in a smarter way
                Console.WriteLine($"{result.Error.Code}: OpenAI = {result.Error.Message}");
                throw new Exception("API Failure");
            }
        }

        static string GeneratePrompt(Task plan, bool showPrompt) {
            if (plan.planLevel > 0  && ExpandMode == ExpandModeType.ONE_BY_ONE) {
                // var prompt =  $"Your job is to {plan.parent.description}. Your current task is to {plan.description}. Please specify a numbered list of the work that needs to be done.";
                //var prompt = $"Please specify a numbered list of the work that needs to be done to {plan.description} when you {basePlan.description}";
                //var prompt = $"Please specify one or two steps that needs to be done to {plan.description} when you {basePlan.description}";
                var prompt = $"Your task is to {basePlan.description}. Repeat the list and add {ExpandSubtaskCount} subtasks to each of the items.\n\n";
                prompt += Util.GetNumberedSteps(plan);
                prompt += "END LIST";
                plan.prompt = prompt;
                if (showPrompt) {
                    Util.WriteToConsole($"\n{prompt}\n", ConsoleColor.Cyan);
                }
                return prompt;
            }
            else if (plan.subTaskDescriptions.Count > 0 && ExpandMode == ExpandModeType.AS_A_LIST) {
                /*
                var prompt =  $"Your job is to {plan.description}. You have identified the following steps:\n";
                prompt += Util.GetNumberedSteps(plan);
                prompt += "Please specify a bulleted list of the work that needs to be done for each step.";
                */
                var prompt = $"Below is part of a plan to {basePlan.description}. Repeat the list and add {ExpandSubtaskCount} subtasks to each of the items\n\n";
                prompt += Util.GetNumberedSteps(plan);
                prompt += "END LIST";
                plan.prompt = prompt;
                if (showPrompt) {
                    Util.WriteToConsole($"\n{prompt}\n", ConsoleColor.Cyan);
                }
                return prompt;
            }
            var firstPrompt =  $"Your job is to {plan.description}. Please specify a numbered list of brief tasks that needs to be done.";
            plan.prompt = firstPrompt;
            if (showPrompt) {
                Util.WriteToConsole($"\n{firstPrompt}\n", ConsoleColor.Cyan);
            }
            return firstPrompt;
        }
    }
  }