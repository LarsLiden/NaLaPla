namespace NaLaPla
{
    //testing push

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
            Console.WriteLine("What do you want to plan?");
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
            if (plan.parent != null) {
                var prompt =  $"Your job is to {plan.parent.description}. Your current task is to {plan.description}. Please specify a numbered list of the work that needs to be done.";
                Console.WriteLine(prompt);
                return prompt;
            }
            var firstPrompt =  $"Your job is to {plan.description}. Please specify a numbered list of the work that needs to be done.";
            Console.WriteLine(firstPrompt);
            return firstPrompt;
        }
    }
  }