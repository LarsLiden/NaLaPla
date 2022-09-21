namespace NaLaPla
{
    public static class Util
    {
        public static List<string> ParseList(string itemString) {

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
            return list;
        }

        public static List<string> Test() {
            var parse1 = "1. Choose a name for the company.\n\n2. Create a logo for the company.\n\n3. Develop a business plan.\n\n4. Raise capital.\n\n5. Find a manufacturing partner.\n\n6. Build a prototype.\n\n7. Test the prototype.\n\n8. Launch the production version of the car.\n\n9. Sell the car.\n\n";
            var parse2 = "\n\n1. Research the electric car market. This includes understanding the current landscape of electric car manufacturers, what consumers want in an electric car, and what features are most important to them.\n\n2. Develop a business plan for your electric car company. This should include your company’s mission and vision, as well as your target market and marketing strategy.\n\n3. Raise capital for your electric car company. This may involve seeking out investors or applying for loans.\n\n4. Hire a team of experts to help you build your electric car company. This may include engineers, designers, and marketing professionals.\n\n5. Develop a prototype of your electric car. This should be a working model that can be used to test and refine your design.\n\n6. Launch your electric car company. This includes marketing your car to consumers and setting up a sales and distribution network.";
            var parse3 = "\n\n1. Hire staff\n2. Set up the production line";
            var list = ParseList(parse1);
            list = ParseList(parse2);
            list = ParseList(parse3);
            return list;
        }

        public static void WritePlan(Plan plan) {
            var description = plan.description.PadLeft(plan.description.Length + (5*plan.planLevel));
            Console.WriteLine(description);
            foreach (var subPlan in plan.planSteps) {
                WritePlan(subPlan);
            }
        }
    }
}
