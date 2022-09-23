namespace NaLaPla
{
    using System.Text.RegularExpressions;

    public static class Util
    {
        public static List<string> ParseSubTaskList(string itemString) {

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

            // If GPT gives long description just keep first sentence
            list = list.Select((n) => {
                var sentences = n.Split(".");
                return sentences[0];
            }).ToList();
            return list;
        }

        private static IEnumerable<string> ParseListToLines(string value) {
            int start = 0;
            bool first = true;

            for (int index = 1; ; ++index) {
                string toFind = $"{index}.";

                int next = value.IndexOf(toFind, start);

                if (next < 0) {
                    yield return value.Substring(start).TrimStart().TrimEnd('\r', '\n');
                    break;
                }

                if (!first) {
                    yield return value.Substring(start, next - start).TrimStart().TrimEnd('\r', '\n');
                }

                first = false;
                start = next + toFind.Length;
            }
        }

        // Replace numbered sub bullets (i.e. "2.1", "3.2.1", " 2." with "-" marks)
        private static string NumberToBullet(string text) {
            var bulletText = Regex.Replace(text, @"\d.\d.", "-");
            bulletText = Regex.Replace(bulletText, @"\d.\d", "-");
            return Regex.Replace(bulletText, @" \d.", "-");
        }

        public static void UpdatePlan(Task plan, string gptResponse) {

            // Assume list is like: "1. task1 -subtask1 -subtask2 2. task2 -subtask 1..."
            var bulletedItem = NumberToBullet(gptResponse);
            var list = ParseListToLines(bulletedItem).ToList();

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
            WritePlan(plan);
        }

        public static string GetNumberedSteps(Task plan) {

            var steps = "";
            for (int i = 0; i < plan.subTaskDescriptions.Count; i++) {
                steps += $"{i+1}. {plan.subTaskDescriptions[i]}\n";
            }
            return steps;
        }

        public static List<string> TestParseList() {
            var parse1 = "1. Choose a name for the company.\n\n2. Create a logo for the company.\n\n3. Develop a business plan.\n\n4. Raise capital.\n\n5. Find a manufacturing partner.\n\n6. Build a prototype.\n\n7. Test the prototype.\n\n8. Launch the production version of the car.\n\n9. Sell the car.\n\n";
            var parse2 = "\n\n1. Research the electric car market. This includes understanding the current landscape of electric car manufacturers, what consumers want in an electric car, and what features are most important to them.\n\n2. Develop a business plan for your electric car company. This should include your company’s mission and vision, as well as your target market and marketing strategy.\n\n3. Raise capital for your electric car company. This may involve seeking out investors or applying for loans.\n\n4. Hire a team of experts to help you build your electric car company. This may include engineers, designers, and marketing professionals.\n\n5. Develop a prototype of your electric car. This should be a working model that can be used to test and refine your design.\n\n6. Launch your electric car company. This includes marketing your car to consumers and setting up a sales and distribution network.";
            var parse3 = "\n\n1. Hire staff\n2. Set up the production line";
            var list = ParseSubTaskList(parse1);
            list = ParseSubTaskList(parse2);
            list = ParseSubTaskList(parse3);
            return list;
        }

        static Task MakeTestPlan(List<string> items) {
            var basePlan = new Task() {
                description = "build a log cabin",
                planLevel = 0, 
                subTasks = new List<Task>()
                };
            foreach (var item in items) {
                var subPlan = new Task() {
                description = item,
                planLevel = 0, 
                subTasks = new List<Task>()
                };
                basePlan.subTasks.Add(subPlan);
            }
            return basePlan;
        }

        public static void TestParseMultiList() {

            var parse1 = "\n\n1. Cut logs to size - use a saw to cut the logs to the desired length\n2. Notch logs for connecting - use a saw to cut notches into the ends of the logs that will fit together\n3. Connect logs at the corners - use nails or screws to attach the logs at the corners\n4. Fill in gaps between logs with chinking - use a chinking material to fill in the gaps between the logs\n5. Add a roof - use roofing material to cover the top of the cabin";
            var plan1Items = new List<string> {"Cut logs to size", "Notch logs for connecting", "Connect logs at the corners", "Fill in gaps between logs with chinking", "Add a roof"};
            var plan1 = MakeTestPlan(plan1Items);
            UpdatePlan(plan1, parse1);

            var parse2 = "\n\n1. Research the electric car market. Understand the competition, what consumers want, and what might be missing in the current market.\n-Analyze the current electric car market\n-Identify the major players and their market share\n-Understand what consumers want in an electric car\n-Determine what is missing in the current market\n\n2. Develop a business plan for your electric car company. This should include your company’s mission, vision, and values, as well as your marketing and sales strategy.\n-Create a mission statement for your company\n-Develop a vision for your company\n-Identify your company values\n-Outline your marketing and sales strategy\n\n3. Create a prototype of your electric car. This will be used to test and validate your design and engineering.\n-Design the prototype of your electric car\n-Build the prototype of your electric car\n-Test the prototype of your electric car\n\n4. Raise capital to fund your electric car company. This can be done through venture capitalists, angel investors, or crowdfunding.\n-Identify the amount of capital needed to fund your company\n-Research different funding options\n-Pitch your company to potential investors\n\n5. Build a team of passionate and talented individuals to help you bring your electric car company to life. This team should include engineers, designers, marketers, and salespeople.\n-Recruit engineers, designers, marketers, and salespeople\n-Hire a team of passionate and talented individuals\n\n6. Launch your electric car company and begin selling cars to consumers. This will require a strong marketing and sales strategy, as well as a well-designed and built car.\n-Develop a marketing and sales strategy\n-Launch your electric car company\n-Sell cars to consumers";
            var plan2Items = new List<string> {
                "Research the electric car market. Understand the competition, what consumers want, and what might be missing in the current market.",
                "Develop a business plan for your electric car company. This should include your company's mission, vision, and values, as well as your marketing and sales strategy.",
                "Create a prototype of your electric car. This will be used to test and validate your design and engineering.",
                "Raise capital to fund your electric car company. This can be done through venture capitalists, angel investors, or crowdfunding.",
                "Build a team of passionate and talented individuals to help you bring your electric car company to life. This team should include engineers, designers, marketers, and salespeople.",
                "Launch your electric car company and begin selling cars to consumers. This will require a strong marketing and sales strategy, as well as a well-designed and built car."
                };
            var plan2 = MakeTestPlan(plan2Items);
            UpdatePlan(plan2, parse2);
        }

        public static void WritePlan(Task plan) {
            var description = $"- {plan.description}".PadLeft(plan.description.Length + (5*plan.planLevel));
            Console.WriteLine(description);

            if (plan.subTasks.Any()) {
                foreach (var subPlan in plan.subTasks) {
                    WritePlan(subPlan);
                }
            }
            else {
                foreach (var subTaskDescription in plan.subTaskDescriptions) {
                    Console.WriteLine($"- {subTaskDescription}".PadLeft(subTaskDescription.Length + (5*(plan.planLevel+1))));
                }
            }

        }
    }
}
