namespace NaLaPla
{
    // TODO: Convert these to actual tests
     public static class Test
    {       public static List<string> TestParseList() {
            var parse1 = "1. Choose a name for the company.\n\n2. Create a logo for the company.\n\n3. Develop a business plan.\n\n4. Raise capital.\n\n5. Find a manufacturing partner.\n\n6. Build a prototype.\n\n7. Test the prototype.\n\n8. Launch the production version of the car.\n\n9. Sell the car.\n\n";
            var parse2 = "\n\n1. Research the electric car market. This includes understanding the current landscape of electric car manufacturers, what consumers want in an electric car, and what features are most important to them.\n\n2. Develop a business plan for your electric car company. This should include your company’s mission and vision, as well as your target market and marketing strategy.\n\n3. Raise capital for your electric car company. This may involve seeking out investors or applying for loans.\n\n4. Hire a team of experts to help you build your electric car company. This may include engineers, designers, and marketing professionals.\n\n5. Develop a prototype of your electric car. This should be a working model that can be used to test and refine your design.\n\n6. Launch your electric car company. This includes marketing your car to consumers and setting up a sales and distribution network.";
            var parse3 = "\n\n1. Hire staff\n2. Set up the production line";
            var list = Util.ParseSubTaskList(parse1);
            list = Util.ParseSubTaskList(parse2);
            list = Util.ParseSubTaskList(parse3);
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
            Util.UpdatePlan(plan1, parse1, true);

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
            Util.UpdatePlan(plan2, parse2, true);
        }
    }
}