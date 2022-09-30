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

    enum ExpandModeType
    {
        ONE_BY_ONE,
        AS_A_LIST    
    }

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
    }

    class Program {

        const int MAX_PROMPT_SIZE = 2000;

        static Plan ?basePlan;
        static int GPTRequestsTotal = 0;

        static ExpandModeType ExpandMode = ExpandModeType.AS_A_LIST;

        static RuntimeConfig configuration = new RuntimeConfig(); // For now just has hardcoded defaults
        static SemaphoreSlim GPTSemaphore = new SemaphoreSlim(configuration.maxConcurrentGPTRequests,configuration.maxConcurrentGPTRequests);

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
                Util.WriteToConsole($"{configuration.ToString()}", ConsoleColor.Green);

                Console.WriteLine("What do you want to plan?");
                var userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) {
                    bail = true;
                } else {
                    (string planDescription, List<FlagType> flags) = ParseUserInput(userInput);

                    if (!configuration.shouldLoadPlan) {
                        Console.WriteLine($"Loading {planDescription}");
                        basePlan = Util.LoadPlan(planDescription); 
                        bail = true;
                    }

                    if (configuration.indexToBuild != "") {
                        try {
                            IR.CreateIndex(new MineCraftDataProvider(configuration.indexToBuild));
                        }
                        catch (Exception e) {
                            Util.WriteToConsole("Failed to create Index", ConsoleColor.Red);
                            Util.WriteToConsole(e.Message.ToString(), ConsoleColor.DarkRed);
                        }
                    }

                    if (!String.IsNullOrEmpty(planDescription)) {
                        basePlan = new Plan() {
                        description = planDescription,
                        planLevel = 0, 
                        subPlans = new List<Plan>()
                        };
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
            Util.PrintPlanToConsole(basePlan, configuration, runData);
            var textFileName = Util.SavePlanAsText(basePlan, configuration, runData);
            Util.SavePlanAsJSON(basePlan);

            // Do post processing steps
            var planString = Util.PlanToString(basePlan);
            for (int i=0;i<PostProcessingPrompts.Count;i++) {
                var postPromptToUse = PostProcessingPrompts[i];
                var prompt = $"{postPromptToUse}{Environment.NewLine}START LIST{Environment.NewLine}{planString}{Environment.NewLine}END LIST";

                // Expand Max Tokens to cover size of plan
                var postPrompt = new Prompt(prompt, configuration);
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
            flags.AddRange(TryGetFlag(pieces, FlagType.DEPTH, ref configuration.expandDepth));
            flags.AddRange(TryGetFlag(pieces, FlagType.TEMP, ref configuration.temperature));
            flags.AddRange(TryGetFlag(pieces, FlagType.TEMPMULT, ref configuration.tempMultPerLevel));
            flags.AddRange(TryGetFlag(pieces, FlagType.MAXGPT, ref configuration.maxConcurrentGPTRequests));
            flags.AddRange(TryGetFlag(pieces, FlagType.SUBTASKS, ref configuration.subtaskCount));

            // Actions
            flags.AddRange(TryGetFlag(pieces, FlagType.LOAD, ref configuration.shouldLoadPlan));
            flags.AddRange(TryGetFlag(pieces, FlagType.INDEX, ref configuration.indexToBuild));
            return (planDescription, flags);
        }

        static async System.Threading.Tasks.Task ExpandPlan(Plan planToExpand) {
            if (basePlan is null) {
                throw new Exception("Got null basePlan");
            }
            if (planToExpand.planLevel > configuration.expandDepth) {
                planToExpand.state = PlanState.FINAL;
                return;
            }
            planToExpand.state = PlanState.GPT_PROMPT_SUBMITTED;
            Util.DisplayProgress(basePlan, configuration, GPTSemaphore);
            var gptResponseCount = await ExpandPlanWithGPT(planToExpand);

            Response bestResponse;
            if (planToExpand.prompt is null) {
                throw new Exception("Got null prompt");
            }
            if (gptResponseCount > 0) {
                bestResponse = await GetBestResponse(basePlan, planToExpand.prompt.responses);
            } else {
                bestResponse = planToExpand.prompt.responses.First();
            }

            planToExpand.state = PlanState.PROCESSING;
            // If one sub item at a time, create children and then expand with
            if (ExpandMode == ExpandModeType.ONE_BY_ONE) {
                
                planToExpand.subPlanDescriptions = Util.ParseSubPlanList(bestResponse.ToString());

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
                if (configuration.maxConcurrentGPTRequests > 1) {
                    var plans = planToExpand.subPlans.Select(async subPlan =>
                    {
                            await ExpandPlan(subPlan);
                    });
                    await System.Threading.Tasks.Task.WhenAll(plans);
                } else {
                    foreach (var subPlan in planToExpand.subPlans) {
                        await ExpandPlan(subPlan);
                    }
                }
            }
            // Otherwise, expand all at once and then create children
            else if (ExpandMode == ExpandModeType.AS_A_LIST) {

                if (planToExpand.subPlanDescriptions.Count == 0) {
                    planToExpand.subPlanDescriptions = Util.ParseSubPlanList(bestResponse.ToString());
                    await ExpandPlan(planToExpand);
                }
                else {
                    UpdatePlan(planToExpand, bestResponse.ToString());

                    // If I haven't reached the end of the plan
                    if (planToExpand.subPlans.Count > 0 ) {
                        if (configuration.maxConcurrentGPTRequests > 1) {
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
            Util.DisplayProgress(basePlan, configuration, GPTSemaphore);
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
                var steps = item.Split(" -").ToList().Select(s => s.TrimStart().TrimEnd(' ', '\r', '\n')).ToList();

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

        static async Task<Response> GetBestResponse(Plan plan, List<Response> responses) {
            if (basePlan is null) {
                throw new Exception("Got null basePlan");
            }
            if (responses.Count == 1) {
                return responses.First();
            }
            
            // If plans are all the same don't need to run query
            bool isAllEqual = responses.Distinct().Count() == 1;
            if (isAllEqual) {
                return responses.First();
            }

            var prompt = $"Which plan is a better for a computer program to ${basePlan.description}?/n";

            foreach (var data in responses.Select((response, index) => (response, index)))
            {
                prompt += $"START PLAN {data.index +1}\n{data.response.ToString().Trim()}\nEND PLAN {data.index +1}\n";
            }
            //Console.WriteLine(prompt);

            var bestPrompt = new Prompt(prompt, configuration);

            // Change: the actual responses will be in bestPrompt.responses
            var resultCount = await GetGPTResponses(bestPrompt);

            // Use voting mechanism
            int[] votes = new int[responses.Count];

            for (int i=0;i<responses.Count;i++) {
                foreach (var r in bestPrompt.responses) {
                    if (r.ToString().ToUpper().Contains($"PLAN {i+1}")) {
                        responses[i].score++;
                    }
                }
            }

            // Get response with highest score
            var bestResponse = responses.MaxBy(x => x.score);
            if (bestResponse is null) {
                throw new Exception("Couldn't get highest score");
            }
            var index = responses.IndexOf(bestResponse);
            // Util.WriteToConsole($"Winner #{index+1}, score = {bestResponse.score}, r = {bestResponse.ToString()}", ConsoleColor.Red);

            return bestResponse;
        }

        static async Task<int> ExpandPlanWithGPT(Plan plan) {
            var promptText = GenerateExpandPrompt(plan);
            plan.prompt = new Prompt(promptText, configuration);

            // Scale the temperature based on plan level
            var levelMult = ((float)Math.Pow(configuration.tempMultPerLevel, plan.planLevel));
            plan.prompt.OAIConfig.Temperature = Math.Clamp(plan.prompt.OAIConfig.Temperature * levelMult, 0, 1.0f);

            var gptResponseCount = await GetGPTResponses(plan.prompt);
            return gptResponseCount;
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
                        Response r = new Response(ResponseType.GPT3, plan.Trim());
                        prompt.responses.Add(r);
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

        // Convert list of plan subtasks into a list
        static string GetNumberedSubTasksAsString(Plan plan) {
                var list = "START LIST\n";
                list += Util.GetNumberedSteps(plan);
                list += "END LIST";
                return list;
        }
        static string GenerateExpandPrompt(Plan plan) {

            string prompt;
            string numberedSubTasksAsString = "";

            if (plan.subPlanDescriptions.Count > 0) {
                numberedSubTasksAsString = GetNumberedSubTasksAsString(plan);
            }
            var description = (basePlan is null || basePlan.description is null) ? "fire your lead developer" : basePlan.description;
            if (plan.planLevel > 0  && ExpandMode == ExpandModeType.ONE_BY_ONE) {
                // var prompt =  $"Your job is to {plan.parent.description}. Your current task is to {plan.description}. Please specify a numbered list of the work that needs to be done.";
                //var prompt = $"Please specify a numbered list of the work that needs to be done to {plan.description} when you {basePlan.description}";
                //var prompt = $"Please specify one or two steps that needs to be done to {plan.description} when you {basePlan.description}";
                prompt = $"Your task is to {description}. Repeat the list and add {configuration.subtaskCount} subtasks to each of the items.\n\n";
                prompt += numberedSubTasksAsString;
            }
            else if (plan.subPlanDescriptions.Count > 0 && ExpandMode == ExpandModeType.AS_A_LIST) {
                /*
                var prompt =  $"Your job is to {plan.description}. You have identified the following steps:\n";
                prompt += Util.GetNumberedSteps(plan);
                prompt += "Please specify a bulleted list of the work that needs to be done for each step.";
                */
                prompt = $"Below are instruction for a computer agent to {description}. Repeat the list and add {configuration.subtaskCount} subtasks to each of the items.\n\n";// in cases where the computer agent could use detail\n\n";
                prompt += numberedSubTasksAsString;
            }
            else {
                prompt =  $"Your job is to provide instructions for a computer agent to {plan.description}. Please specify a numbered list of {configuration.subtaskCount} brief tasks that needs to be done.";
            }
            
            // Now add grounding 
            if (configuration.useGrounding) {
                var promptSize = Util.NumWordsIn(prompt);
                var maxGrounds = MAX_PROMPT_SIZE - promptSize;

                var documents = IR.GetRelatedDocuments($"{description}\n{numberedSubTasksAsString}", configuration.showGrounding);
                var grounding = "";
                foreach (var document in documents) {
                    grounding += $"{document}{Environment.NewLine}";
                }
                grounding = Util.LimitWordCountTo(grounding, maxGrounds);
                prompt = $"{grounding}{Environment.NewLine}{prompt}";
            }

            if (configuration.showPrompts) {
                Util.WriteToConsole($"\n{prompt}\n", ConsoleColor.Cyan);
            }

            // TODO: This has code smell.  Both setting the returning the prompt
            plan.prompt = new Prompt(prompt,configuration);
            return prompt;
        }
    }
}