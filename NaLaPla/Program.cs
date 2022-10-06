using System.Diagnostics;
namespace NaLaPla
{
    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Configuration.Json;
    using OpenAI.GPT3;
    using OpenAI.GPT3.Managers;
    using OpenAI.GPT3.ObjectModels;
    using OpenAI.GPT3.ObjectModels.ResponseModels;
    using OpenAI.GPT3.ObjectModels.RequestModels;
    using OpenAI.GPT3.Extensions;
    using OpenAI.GPT3.Interfaces;
    using Microsoft.Extensions.Configuration.EnvironmentVariables;
    using System.ComponentModel;

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
        [Description("-SUBTASKS \t<string>\t\tRequest <string> sub-plans")]
        SUBTASKS,        // default subtasks to ask for
        [Description("-TEMP     \t<float 0-1>\t\tDefault temperature")]
        TEMP,            // default temperature]
        [Description("-TEMPMULT\t<float>\t\t\tTemperature multipler per level")]
        TEMPMULT,            // default temperature]        
        [Description("-SHOWGROUND\t<bool>\t\t\tShow grounding info")]
        SHOWGROUND,            // default temperature]
        USEGROUND,            // default temperature]  
        [Description("-SHOWPROMPT\t<bool>\t\t\tShow prompts")]
        SHOWPROMPT,            // default temperature]     
        [Description("-DEFACTOR\t<string>\t\tDefault actor")]
        DEFACTOR,            // default actor]   
        [Description("-DEFCONTEXT\t<string>\t\tDefault context")]
        DEFCONTEXT,            // default temperature]                                              
    }

    class Program {

        static Plan ?basePlan;
        static int GPTRequestsTotal = 0;
        static string settingsFile = Path.Combine(Directory.GetCurrentDirectory(), "settings.json");

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
                new Microsoft.Extensions.Configuration.ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .AddJsonFile(settingsFile, optional:true)
                    .Build();


            if (config.GetSection("RuntimeConfig") is not null) {
                var c = config.GetSection("RuntimeConfig");
                c.Bind(runtimeConfiguration);
            }

            bool bail = false;
            while (bail == false) {
                ShowFlags(runtimeConfiguration.configStringToProperty);
                Util.WriteToConsole($"{runtimeConfiguration.ToString()}", ConsoleColor.Green);

                Console.WriteLine("What do you want to plan?");
                var userInput = Console.ReadLine();
                if (String.IsNullOrEmpty(userInput)) {
                    bail = true;
                } else {
                    string planDescription = ParseUserInput(userInput);

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
                            Util.WriteToConsole("Failed to create Index", ConsoleColor.Red);
                            Util.WriteToConsole(e.Message.ToString(), ConsoleColor.DarkRed);
                        }
                    }

                    if (!String.IsNullOrEmpty(planDescription)) {
                        basePlan = new Plan() {
                        description = planDescription,
                        actor = runtimeConfiguration.defaultActor,
                        context = runtimeConfiguration.defaultContext,
                        planLevel = 0, 
                        subPlans = new List<Plan>()
                        };
                        bail = true;
                    }
                }
                runtimeConfiguration.SaveSettings(settingsFile);
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
        
        static void ShowFlags(List<RuntimeConfig.ConfigVariable> flags) {
            foreach (var flag in flags) {
                Util.WriteToConsole(
                    "-" + flag.cmdLine.PadRight(20) + flag.description,
                    ConsoleColor.Cyan
                );
            }
        }
        static string ParseUserInput(string userInput) {

            var pieces = userInput.Split("-").ToList();
            var planDescription = pieces[0].TrimEnd();
            pieces.RemoveAt(0);

            var flags = new List<FlagType>();
            foreach (var flagAndValue in pieces) {
                var flagStrings = flagAndValue.Split(" ");
                var flagName = flagStrings[0];
                var flagArg = flagStrings.Count() > 1 ? string.Join(" ", flagStrings.Skip(1).Take(999)) : null;                
                runtimeConfiguration.SetValue(flagName, flagArg);
            }

            return planDescription; 
        }

        static async System.Threading.Tasks.Task ExpandPlan(Plan planToExpand) {
            if (basePlan is null) {
                throw new Exception("Got null basePlan");
            }
            if (planToExpand.planLevel > runtimeConfiguration.expandDepth) {
                planToExpand.state = PlanState.FINAL;
                return;
            }
            planToExpand.state = PlanState.GPT_PROMPT_SUBMITTED;
            Util.DisplayProgress(basePlan, runtimeConfiguration, GPTSemaphore);
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
            if (runtimeConfiguration.ExpandMode == ExpandModeType.ONE_BY_ONE) {
                
                planToExpand.subPlanDescriptions = Util.ParseSubPlanList(bestResponse.ToString());

                // Create sub-plans
                foreach (var subPlanDescription in planToExpand.subPlanDescriptions) {
                    var plan = new Plan() {
                        description = subPlanDescription,
                        actor = planToExpand.actor,
                        context = planToExpand.context,
                        planLevel = planToExpand.planLevel + 1,
                        subPlans = new List<Plan>(),
                        parent = planToExpand
                    };
                    planToExpand.subPlans.Add(plan);
                }
                if (runtimeConfiguration.maxConcurrentGPTRequests > 1) {
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
            else if (runtimeConfiguration.ExpandMode == ExpandModeType.AS_A_LIST) {

                if (planToExpand.subPlanDescriptions.Count == 0) {
                    planToExpand.subPlanDescriptions = Util.ParseSubPlanList(bestResponse.ToString());
                    await ExpandPlan(planToExpand);
                }
                else {
                    UpdatePlan(planToExpand, bestResponse.ToString());

                    // If I haven't reached the end of the plan
                    if (planToExpand.subPlans.Count > 0 ) {
                        if (runtimeConfiguration.maxConcurrentGPTRequests > 1) {
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
            Util.DisplayProgress(basePlan, runtimeConfiguration, GPTSemaphore);
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
                var steps = item.Replace("\n-", " -").Split(" -").ToList().Select(s => s.TrimStart().TrimEnd(' ', '\r', '\n')).ToList();

                // Check if the plan has bottomed out
                if (steps[0]=="") {
                    plan.subPlanDescriptions = steps;
                    return;
                }
                
                var description = steps[0];
                steps.RemoveAt(0);
                var subPlan = new Plan() {
                        description = description,
                        actor = plan.actor,
                        context = plan.context,
                        planLevel = plan.planLevel + 1, 
                        subPlanDescriptions = steps,
                        subPlans = new List<Plan>(),
                        parent = plan
                    };
                plan.subPlans.Add(subPlan);
            }
            if (runtimeConfiguration.showResults) {
                Util.PrintPlanToConsole(plan,runtimeConfiguration);
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

            var bestPrompt = new Prompt(prompt, runtimeConfiguration);

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

            plan.prompt = new ExpandPrompt(basePlan, plan, runtimeConfiguration);

            // Scale the temperature based on plan level
            var levelMult = ((float)Math.Pow(runtimeConfiguration.tempMultPerLevel, plan.planLevel));
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
    }
}