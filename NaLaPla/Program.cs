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
            WritePlan(basePlan);
        }

        static void WritePlan(Plan plan) {
            var description = plan.description.PadLeft(plan.description.Length + (5*plan.planLevel));
            Console.WriteLine(description);
            foreach (var subPlan in plan.planSteps) {
                WritePlan(subPlan);
            }
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
            var subTasks = ParseList(rawPlan);
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

        
        static List<string> ParseList(string itemString) {

            // Assume list is like: "1. this 2. that 3. other"
            var list = itemString.Split("\n").ToList();

            list = list.Select((n) => {
                var breakPos = n.IndexOf(". ");
                if (breakPos > -1) {
                    return n.Substring(breakPos+2);
                }
                return n;
            }).Distinct().ToList();
            
            list.RemoveAll(s => string.IsNullOrEmpty(s));

            /*
            // Remove the first number
            list.RemoveAt(0);

            char[] ch = {'.', '\n'};
            list = list.Select((n) => {
                var breakPos = n.LastIndexOfAny(ch);
                if (breakPos > -1) {
                    return n.Substring(0, breakPos);
                }
                return n;
            }).Distinct().ToList();
            */
            return list;
        }

        static List<string> Test() {
            var parse1 = "1. Choose a name for the company.\n\n2. Create a logo for the company.\n\n3. Develop a business plan.\n\n4. Raise capital.\n\n5. Find a manufacturing partner.\n\n6. Build a prototype.\n\n7. Test the prototype.\n\n8. Launch the production version of the car.\n\n9. Sell the car.\n\n";
            var parse2 = "\n\n1. Research the electric car market. This includes understanding the current landscape of electric car manufacturers, what consumers want in an electric car, and what features are most important to them.\n\n2. Develop a business plan for your electric car company. This should include your company’s mission and vision, as well as your target market and marketing strategy.\n\n3. Raise capital for your electric car company. This may involve seeking out investors or applying for loans.\n\n4. Hire a team of experts to help you build your electric car company. This may include engineers, designers, and marketing professionals.\n\n5. Develop a prototype of your electric car. This should be a working model that can be used to test and refine your design.\n\n6. Launch your electric car company. This includes marketing your car to consumers and setting up a sales and distribution network.";
            var parse3 = "\n\n1. Hire staff\n2. Set up the production line";
            var list = ParseList(parse1);
            list = ParseList(parse2);
            list = ParseList(parse3);
            return list;
        }
    }

    class Plan {
        public string? description;
        public int planLevel;        
        public List<Plan>? planSteps;

        public Plan? parent;
    }
  }