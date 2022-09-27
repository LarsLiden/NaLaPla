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

        public static List<string> ParseSubTaskList(string itemString) {

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

        // Replace numbered sub bullets (i.e. "2.1", "3.2.1", " 2.", " a." with "-" marks)
        private static string NumberToBullet(string text) {
            var bulletText = Regex.Replace(text, @"\d\.\d.", "-");
            bulletText = Regex.Replace(bulletText, @"\d\.\d", "-");
            bulletText = Regex.Replace(bulletText, @" \d\.", "-");
            return Regex.Replace(bulletText, @" [a-zA-Z]\.", "-");
        }

        public static void UpdatePlan(Task plan, string gptResponse, bool showResults) {

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
            if (showResults) {
                PrintPlanToConsole(plan);
            }
        }

        public static string GetNumberedSteps(Task plan) {

            var steps = "";
            for (int i = 0; i < plan.subTaskDescriptions.Count; i++) {
                steps += $"{i+1}. {plan.subTaskDescriptions[i]}\n";
            }
            return steps;
        }

        public static void WriteToConsole(string text, ConsoleColor color) {
            if (color == null) {
                Console.ForegroundColor = ConsoleColor.White;
            }
            else if (color is ConsoleColor) {
                Console.ForegroundColor = color;
            }
            Console.WriteLine(text);
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static string PlanToString(Task plan) {
            string planText = $"- {plan.description}{Environment.NewLine}".PadLeft(plan.description.Length + (5*plan.planLevel));

            if (plan.subTasks.Any()) {
                foreach (var subPlan in plan.subTasks) {
                    planText += PlanToString(subPlan);
                }
            }
            else {
                foreach (var subTaskDescription in plan.subTaskDescriptions) {
                    string output = $"- {subTaskDescription}{Environment.NewLine}".PadLeft(subTaskDescription.Length + (5*(plan.planLevel+1)));
                    planText += $"{output}";
                }
            }
            return planText;
        }

        public static string PlanToJSON(Task plan) {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(plan);
            return json;
        }        

        public static string LoadString(string planDescription) {
            var planName = GetSaveName(planDescription, TEXT_FILE_EXTENSION);    
            var fileName = $"{planName}.{TEXT_FILE_EXTENSION}";
            var planString = File.ReadAllText($"{SAVE_DIRECTORY}/{fileName}");
            return planString;
        }

        public static Task LoadPlan(string planName) {  
            var fileName = $"{planName}.{PLAN_FILE_EXTENSION}";
            var planString = File.ReadAllText($"{SAVE_DIRECTORY}/{fileName}");
            var plan = Newtonsoft.Json.JsonConvert.DeserializeObject<Task>(planString);
            return plan;
        }

        public static string GetPlanName(Task basePlan) {
            
            var planName = basePlan.description;
                foreach (var c in Path.GetInvalidFileNameChars()) {
                planName.Replace(c.ToString(),"-");
            }
            return planName;
        }

        private static string GetSaveName(Task basePlan, string fileExtension) {
            
            var planName = GetPlanName(basePlan);
            return GetSaveName(planName, fileExtension);
        }

        private static string GetSaveName(string planName, string fileExtension) {
            // If writing to file add counter if file already exits
            var version = $"";
            var myFile = $"";
            while (File.Exists(myFile = $"{SAVE_DIRECTORY}/{planName}{version}.{fileExtension}")) {
                version = (version == "") ? version = "2" : version = (Int32.Parse(version) + 1).ToString();
            }
            planName += version;
            return planName;
        }

        public static void PrintPlanToConsole(Task plan, string configList="", string runData="") {
            var planName = GetPlanName(plan);
            var planString = PlanToString(plan);
            Util.WriteToConsole(planName, ConsoleColor.Green);
            Util.WriteToConsole(configList, ConsoleColor.Green);
            Util.WriteToConsole(runData, ConsoleColor.Green);
            Util.WriteToConsole(planString, ConsoleColor.White);
        }

        public static void SavePlanAsText(Task plan, string configList, string runData) {
            var saveName = GetSaveName(plan, TEXT_FILE_EXTENSION);
            var planString = $"{configList}\n{runData}\n\n";
            planString += PlanToString(plan);
            SaveText(saveName, planString, TEXT_FILE_EXTENSION);
        }

        public static void SavePlanAsJSON(Task plan) {
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

        static List<Task> AllChildren(Task start)
        {
            return DepthFirstTreeTraversal(start, c=>c.subTasks).ToList();
        }

        public static void DisplayProgress(Task basePlan, int GPTRequestsInFlight, bool detailed = false) {
            WriteToConsole($"\n\nProgress ({GPTRequestsInFlight} GPT requests in flight):",ConsoleColor.Blue);
            var all = AllChildren(basePlan);
            foreach (var t in all) {
                var display = $"- {t.description} ({t.state}) ";
                var status = display.PadLeft(display.Length + (5*t.planLevel));
                WriteToConsole(status, ConsoleColor.White);
            }
        }
    }
}
