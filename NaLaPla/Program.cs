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
    
        static Plan ?basePlan;
        static int GPTRequestsTotal = 0;
        static int GPTRequestsInFlight = 0;

        static ExpandModeType ExpandMode = ExpandModeType.AS_A_LIST;
        
        static OpenAIConfig OAIConfig = new OpenAIConfig();

        static RuntimeConfig configuration = new RuntimeConfig(); // For now just has hardcoded defaults

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

            Util.WriteToConsole($"\n\n\n{configuration.ToString()}", ConsoleColor.Green);
            Console.WriteLine("What do you want to plan?");
            var userInput = Console.ReadLine();
            if (String.IsNullOrEmpty(userInput)) return;
            var runTimer = new System.Diagnostics.Stopwatch();
            runTimer.Start();

            (string planDescription, List<FlagType> flags) = ParseUserInput(userInput);

            if (flags.Contains(FlagType.LOAD)) {
                basePlan = Util.LoadPlan(planDescription);
            }
            else {
                basePlan = new Plan() {
                    description = planDescription,
                    planLevel = 0, 
                    subPlans = new List<Plan>()
                    };

                await ExpandPlan(basePlan);
                runTimer.Stop();

                var runData = $"Runtime: {runTimer.Elapsed.ToString(@"m\:ss")}, GPT requests: {GPTRequestsTotal}\n";

                // Output plan
                Util.PrintPlanToConsole(basePlan, configuration, runData);
                Util.SavePlanAsText(basePlan, configuration, runData);
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
                            configuration.expandDepth = expandDepth;
                        }
                    }
                    flags.Add(flag);
                }
            }

            return (PlanDescription: planDescription, Flags: flags);
        }

        static async System.Threading.Tasks.Task ExpandPlan(Plan planToExpand) {

            if (planToExpand.planLevel > configuration.expandDepth) {
                planToExpand.state = PlanState.FINAL;
                return;
            }
            planToExpand.state = PlanState.GPT_PROMPT_SUBMITTED;
            Util.DisplayProgress(basePlan, GPTRequestsInFlight);
            var gptResponses = await ExpandPlanWithGPT(planToExpand);

            string bestResponse;
            if (gptResponses.Count > 0) {
                bestResponse = await GetBestPlan(basePlan, gptResponses);
            } else {
                bestResponse = gptResponses.First();
            }

            planToExpand.GPTresponse = bestResponse;
            planToExpand.state = PlanState.PROCESSING;
            // If one sub item at a time, create children and then expand with
            if (ExpandMode == ExpandModeType.ONE_BY_ONE) {
                
                planToExpand.subPlanDescriptions = Util.ParseSubPlanList(bestResponse);

                // Create sub-plans
                foreach (var subPlanDescription in planToExpand.subPlanDescriptions) {
                    var plan = new Plan() {
                        description = subPlanDescription,
                        planLevel = planToExpand.planLevel + 1,
                        subPlans = new List<Plan>(),
                        parent = planToExpand
                    };
                    planToExpand.subPlans.Add(plan);
                }
                if (configuration.parallelGPTRequests) {
                    var tasks = planToExpand.subPlans.Select(async subPlan =>
                    {
                            await ExpandPlan(subPlan);
                    });
                    await System.Threading.Tasks.Task.WhenAll(tasks);
                } else {
                    foreach (var subPlan in planToExpand.subPlans) {
                        await ExpandPlan(subPlan);
                    }
                }
            }
            // Otherwise, expand all at once and then create children
            else if (ExpandMode == ExpandModeType.AS_A_LIST) {

                if (planToExpand.subPlanDescriptions.Count == 0) {
                    planToExpand.subPlanDescriptions = Util.ParseSubPlanList(bestResponse);
                    await ExpandPlan(planToExpand);
                }
                else {
                    // Only request a display if we're not using parallel requests
                    UpdatePlan(planToExpand, bestResponse);

                    // If I haven't reached the end of the plan
                    if (planToExpand.subPlans.Count > 0 ) {
                        if (configuration.parallelGPTRequests) {
                            var plans = planToExpand.subPlans.Select(async subPlan =>
                            {
                                if (subPlan.subPlanDescriptions.Any()) {
                                    await ExpandPlan(subPlan);
                                }
                            });
                            await System.Threading.Tasks.Task.WhenAll(plans);
                        } else {
                            foreach (var subPlan in planToExpand.subPlans) {
                                if (subPlan.subPlanDescriptions.Any()) {
                                    await ExpandPlan(subPlan);
                                }
                            }
                        }
                    }
                }
            }
            planToExpand.state = PlanState.DONE;
            Util.DisplayProgress(basePlan,GPTRequestsInFlight);
        }

        public static void UpdatePlan(Plan plan, string gptResponse) {

            // Assume list is like: "1. task1 -subtask1 -subtask2 2. task2 -subtask 1..."
            var bulletedItem = Util.NumberToBullet(gptResponse);
            var list = Util.ParseListToLines(bulletedItem).ToList();

            // When GPT can't find any more subtasks, it just add to the end of the list
            if (list.Count() > plan.subPlanDescriptions.Count()) {
                return;
            }
            plan.subPlans = new List<Plan>();
            foreach (var item in list) {
                var steps = item.Split("-").ToList().Select(s => s.TrimStart().TrimEnd(' ', '\r', '\n')).ToList();

                // Check if the plan has bottomed out
                if (steps[0]=="") {
                    plan.subPlanDescriptions = steps;
                    return;
                }
                
                var description = steps[0];
                steps.RemoveAt(0);
                var subPlan = new Plan() {
                        description = description,
                        planLevel = plan.planLevel + 1, 
                        subPlanDescriptions = steps,
                        subPlans = new List<Plan>()
                    };
                plan.subPlans.Add(subPlan);
            }
            if (configuration.showResults) {
                Util.PrintPlanToConsole(plan,configuration);
            }
        }

        static async Task<string> GetBestPlan(Plan plan, List<string> plans) {

            if (plans.Count == 1) {
                return plans.First();
            }
            
            // If plans are all the same don't need to run query
            bool isAllEqual = plans.Distinct().Count() == 1;
            if (isAllEqual) {
                return plans.First();
            }


            var prompt = $"Which plan is a better for a computer program to ${basePlan.description}\n?";

            foreach (var data in plans.Select((plan, index) => (plan, index)))
            {
                prompt += $"START PLAN {data.index +1}\n{data.plan}\nEND PLAN {data.index +1}\n";
            }

            var results = await GetGPTResponses(prompt);

            // Use voting mechanism
            int[] votes = new int[plans.Count];

            for (int i=0;i<plans.Count;i++) {
                foreach (var result in results) {
                    if (result.ToUpper().Contains($"PLAN {i+1}")) {
                        votes[i]++;
                    }
                }
            }

            // Get max index 
            int maxValue = votes.Max();
            int maxIndex = votes.ToList().IndexOf(maxValue);

            Util.WriteToConsole($"Winner #{maxIndex+1}", ConsoleColor.Red);

            return plans[maxIndex];
        }

        static async Task<List<string>> ExpandPlanWithGPT(Plan plan) {
            var prompt = GenerateExpandPrompt(plan);
            var gptresponses = await GetGPTResponses(prompt);
            return gptresponses;
        }

        // Return list of candidate plans
        static async Task<List<string>> GetGPTResponses(string prompt) {
            var apiKey = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            if (apiKey is null) {
                throw new Exception("Please specify api key in .env");
            }

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
                var rawPlans = result.Choices.Select(c => c.Text).ToList();
                GPTRequestsTotal++;
                return rawPlans;
            }
            else {
                // TODO: Handle failures in a smarter way
                if (result.Error is not null) {
                    Console.WriteLine($"{result.Error.Code}: OpenAI = {result.Error.Message}");
                }
                throw new Exception("API Failure");
            }
        }

        static string GenerateExpandPrompt(Plan plan) {
            var description = (basePlan is null || basePlan.description is null) ? "fire your lead developer" : basePlan.description;
            if (plan.planLevel > 0  && ExpandMode == ExpandModeType.ONE_BY_ONE) {
                // var prompt =  $"Your job is to {plan.parent.description}. Your current task is to {plan.description}. Please specify a numbered list of the work that needs to be done.";
                //var prompt = $"Please specify a numbered list of the work that needs to be done to {plan.description} when you {basePlan.description}";
                //var prompt = $"Please specify one or two steps that needs to be done to {plan.description} when you {basePlan.description}";
                var prompt = $"Your task is to {description}. Repeat the list and add {configuration.subtaskCount} subtasks to each of the items.\n\n";
                prompt += Util.GetNumberedSteps(plan);
                prompt += "END LIST";
                plan.prompt = prompt;
                if (configuration.showPrompts) {
                    Util.WriteToConsole($"\n{prompt}\n", ConsoleColor.Cyan);
                }
                return prompt;
            }
            else if (plan.subPlanDescriptions.Count > 0 && ExpandMode == ExpandModeType.AS_A_LIST) {
                /*
                var prompt =  $"Your job is to {plan.description}. You have identified the following steps:\n";
                prompt += Util.GetNumberedSteps(plan);
                prompt += "Please specify a bulleted list of the work that needs to be done for each step.";
                */
                var prompt = $"Below are instruction for a computer agent to {description}. Repeat the list and add {configuration.subtaskCount} subtasks to each of the items.\n\n";// in cases where the computer agent could use detail\n\n";
                prompt += Util.GetNumberedSteps(plan);
                prompt += "END LIST";
                plan.prompt = prompt;
                if (configuration.showPrompts) {
                    Util.WriteToConsole($"\n{prompt}\n", ConsoleColor.Cyan);
                }
                return prompt;
            }
            var firstPrompt =  $"Your job is to provide instructions for a computer agent to {plan.description}. Please specify a numbered list of {configuration.subtaskCount} brief tasks that needs to be done.";
            plan.prompt = firstPrompt;
            if (configuration.showPrompts) {
                Util.WriteToConsole($"\n{firstPrompt}\n", ConsoleColor.Cyan);
            }
            return firstPrompt;
        }
   
    }
  }