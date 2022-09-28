namespace NaLaPla
{
    using System.Net;
    using System;
    using System.IO;
    using System.Text.RegularExpressions;

    public static class Util
    {
        const string TEXT_FILE_EXTENSION = "txt";

        const string PLAN_FILE_EXTENSION = "plan";

        const string SAVE_DIRECTORY = "output";

        public static List<string> ParseSubPlanList(string itemString) {

            // Assume list is like: "1. this 2. that 3. other"
            var list = itemString.Split('\r', '\n').ToList();

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

        public static IEnumerable<string> ParseListToLines(string value) {
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

        // Replace numbered sub bullets (i.e. "2.1", "3.2.1", " 2.", " a." with "-" marks)
        public static string NumberToBullet(string text) {
            var bulletText = Regex.Replace(text, @"\d\.\d.", "-");
            bulletText = Regex.Replace(bulletText, @"\d\.\d", "-");
            bulletText = Regex.Replace(bulletText, @" \d\.", "-");
            return Regex.Replace(bulletText, @" [a-zA-Z]\.", "-");
        }

        public static string GetNumberedSteps(Plan plan) {

            var steps = "";
            for (int i = 0; i < plan.subPlanDescriptions.Count; i++) {
                steps += $"{i+1}. {plan.subPlanDescriptions[i]}\n";
            }
            return steps;
        }

        public static void WriteToConsole(string text, ConsoleColor color = ConsoleColor.White) {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static string PlanToString(Plan plan) {
            string planText = $"- {plan.description}{Environment.NewLine}".PadLeft(plan.description.Length + (5*plan.planLevel));

            if (plan.subPlans.Any()) {
                foreach (var subPlan in plan.subPlans) {
                    planText += PlanToString(subPlan);
                }
            }
            else {
                foreach (var subPlanDescription in plan.subPlanDescriptions) {
                    string output = $"- {subPlanDescription}{Environment.NewLine}".PadLeft(subPlanDescription.Length + (5*(plan.planLevel+1)));
                    planText += $"{output}";
                }
            }
            return planText;
        }

        public static string PlanToJSON(Plan plan) {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(plan);
            return json;
        }        

        public static string LoadString(string planDescription) {
            var planName = GetSaveName(planDescription, TEXT_FILE_EXTENSION);    
            var fileName = $"{planName}.{TEXT_FILE_EXTENSION}";
            var planString = File.ReadAllText($"{SAVE_DIRECTORY}/{fileName}");
            return planString;
        }

        public static Plan LoadPlan(string planName) {  
            var fileName = $"{planName}.{PLAN_FILE_EXTENSION}";
            var planString = File.ReadAllText($"{SAVE_DIRECTORY}/{fileName}");
            var plan = Newtonsoft.Json.JsonConvert.DeserializeObject<Plan>(planString);
            if (plan is null) {
                throw new Exception("Null plan loaded");
            }
            return plan;
        }

        public static string GetPlanName(Plan basePlan) {
            
            var planName = (basePlan.description is null) ? "--unknown--" : basePlan.description;
            foreach (var c in Path.GetInvalidFileNameChars()) {
                planName.Replace(c.ToString(),"-");
            }
            return planName;
        }

        private static string GetSaveName(Plan basePlan, string fileExtension) {
            
            var planName = GetPlanName(basePlan);
            return GetSaveName(planName, fileExtension);
        }

        private static string GetSaveName(string planName, string fileExtension) {
            // If writing to file add counter if file already exits
            var version = $"";
            var myFile = $"";
            if (planName is null) {
                planName = "--unknown--";
            }
            while (File.Exists(myFile = $"{SAVE_DIRECTORY}/{planName}{version}.{fileExtension}")) {
                version = (version == "") ? version = "2" : version = (Int32.Parse(version) + 1).ToString();
            }
            planName += version;
            return planName;
        }

        public static void PrintPlanToConsole(Plan plan, RuntimeConfig configuration, string runData="") {
            var planName = GetPlanName(plan);
            var planString = PlanToString(plan);
            Util.WriteToConsole(planName, ConsoleColor.Green);
            Util.WriteToConsole(configuration.ToString(), ConsoleColor.Green);
            Util.WriteToConsole(runData, ConsoleColor.Green);
            Util.WriteToConsole(planString, ConsoleColor.White);
        }

        public static void SavePlanAsText(Plan plan, RuntimeConfig configuration, string runData) {
            var saveName = GetSaveName(plan, TEXT_FILE_EXTENSION);
            var planString = $"{configuration.ToString()}\n{runData}\n\n";
            planString += PlanToString(plan);
            SaveText(saveName, planString, TEXT_FILE_EXTENSION);
        }

        public static void SavePlanAsJSON(Plan plan) {
            var saveName = GetSaveName(plan, PLAN_FILE_EXTENSION);
            var planString = PlanToJSON(plan);
            SaveText(saveName, planString, PLAN_FILE_EXTENSION);
        }

        public static void SaveText(string fileName, string text, string extension = TEXT_FILE_EXTENSION) {
            bool exists = System.IO.Directory.Exists(SAVE_DIRECTORY);
            if(!exists) {
                System.IO.Directory.CreateDirectory(SAVE_DIRECTORY);
            }
            var writer = new StreamWriter($"{SAVE_DIRECTORY}/{fileName}.{extension}");
            writer.Write(text);
            writer.Close();
        }

        static IEnumerable<T> DepthFirstTreeTraversal<T>(T root, Func<T, IEnumerable<T>> children)      
        {
            var stack = new Stack<T>();
            stack.Push(root);
            while(stack.Count != 0)
            {
                var current = stack.Pop();
                // If you don't care about maintaining child order then remove the Reverse.
                foreach(var child in children(current).Reverse())
                    stack.Push(child);
                yield return current;
            }
        }

        static List<Plan> AllChildren(Plan start)
        {
            return DepthFirstTreeTraversal(start, c=>c.subPlans).ToList();
        }

        public static void DisplayProgress(Plan? basePlan, RuntimeConfig configuration, SemaphoreSlim GPTSemaphore, bool detailed = false) {
            if (basePlan is null) return;
            WriteToConsole($"\n\nProgress ({configuration.maxConcurrentGPTRequests - GPTSemaphore.CurrentCount} GPT requests in flight):",ConsoleColor.Blue);
            var all = AllChildren(basePlan);
            foreach (var t in all) {
                var display = $"- {t.description} ({GetDescription(t.state)}) ";
                var status = display.PadLeft(display.Length + (5*t.planLevel));
                WriteToConsole(status, ConsoleColor.White);
            }
        }

        public static string GetDescription<T>(this T enumerationValue)
            where T : struct
        {
            Type type = enumerationValue.GetType();
            if (!type.IsEnum)
            {
                throw new ArgumentException("EnumerationValue must be of Enum type", "enumerationValue");
            }

            //Tries to find a DescriptionAttribute for a potential friendly name
            //for the enum
            var enumString = enumerationValue.ToString();
            enumString = String.IsNullOrEmpty(enumString) ? "<unknown>" : enumString;
            System.Reflection.MemberInfo[] memberInfo = type.GetMember(enumString);
            if (memberInfo != null && memberInfo.Length > 0)
            {
                object[] attrs = memberInfo[0].GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);

                if (attrs != null && attrs.Length > 0)
                {
                    //Pull out the description value
                    return ((System.ComponentModel.DescriptionAttribute)attrs[0]).Description;
                }
            }
            //If we have no description attribute, just return the ToString of the enum
            return enumString;
        }        
    }
}
