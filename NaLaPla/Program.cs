namespace NaLaPla
{
    // testing

    using Microsoft.Extensions.Configuration;
    using Microsoft.Extensions.Configuration.EnvironmentVariables;

    class Program
    {
        static Plan basePlan;

        static async Task Main(string[] args)
        {
            var root = Directory.GetCurrentDirectory();
            var dotenv = Path.Combine(root, ".env");
            DotEnv.Load(dotenv);

            var config =
                new ConfigurationBuilder()
                    .AddEnvironmentVariables()
                    .Build();

            //Test();
           // return;
            Console.WriteLine("What do you want instructions for?");
            String plan = Console.ReadLine();

            basePlan = new Plan() {
                description = plan,
                planLevel = 0, 
                planSteps = new List<Plan>()
                };

            await ExpandPlan(basePlan);
            Util.WritePlan(basePlan);
        }

        static async Task ExpandPlan(Plan planToExpand) {

            if (planToExpand.planLevel > 1) {
                return;
            }
            var subTasks = await GetSubTasks(planToExpand);
            foreach (var subTask in subTasks) {
                var subPlan = new Plan() {
                    description = subTask,
                    planLevel = planToExpand.planLevel + 1,
                    planSteps = new List<Plan>(),
                    parent = planToExpand
                };
                planToExpand.planSteps.Add(subPlan);
            }

            foreach (var subPlan in planToExpand.planSteps) {
                await ExpandPlan(subPlan);
            }
        }

        static async Task<List<string>> GetSubTasks(Plan plan) {
            var apiKey = System.Environment.GetEnvironmentVariable("OPENAI_API_KEY");
            var api = new OpenAI_API.OpenAIAPI(apiKey, "text-davinci-002");
            var prompt = GeneratePrompt(plan);
            OpenAI_API.CompletionResult result = await api.Completions.CreateCompletionAsync(
                prompt,
                max_tokens: 500,
                temperature: 0.6);

            var rawPlan = result.ToString();
            var subTasks = Util.ParseList(rawPlan);
            return subTasks;
        }

        static string GeneratePrompt(Plan plan) {
            var prompt = $""; // Might be useful to give background or context on intent of plan here
            var excludeSiblings = true; // This isn't working very well right now.

            if (plan.parent != null) {
                prompt =  $"Your job is to {plan.parent.description}. Your current task is to {plan.description}. ";
            } else {
                prompt =  $"Your job is to {plan.description}.";
            }
            var excludePrompt = $"";
            if (excludeSiblings) {
                excludePrompt = GenerateExcludes(plan);
            }
            var outputInstructions = $" Please specify a list of the work that needs to be done. Format as a numbered list.";
            var fullPrompt = prompt + excludePrompt + outputInstructions;
            Console.WriteLine(fullPrompt);
            return fullPrompt;
        }

        static string GenerateExcludes(Plan plan) {
            var excludePrompt = $"";
            if (plan.parent != null) {
                // Exclude steps that are siblings to this one
                excludePrompt = $" These things have already been planned: ";
                var excludes = new List<string>();
                foreach (var sibling in plan.parent.planSteps) {
                    if (sibling != plan) {
                        excludes.Add(sibling.description);
                    }
                }
                excludePrompt += string.Join(", ",excludes.ToArray()) + ".";
            } 
            return excludePrompt;
        }
    }
  }