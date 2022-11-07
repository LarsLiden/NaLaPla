namespace NaLaPla
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;

    public static class Util
    {
        const string TEXT_FILE_EXTENSION = "txt";

        const string PLAN_FILE_EXTENSION = "plan";

        const string SAVE_DIRECTORY = "output";


        public static string CleanListString(string listString) {
            return listString.Replace("\r\n", "\n").Replace("\n\n", "\n").Trim();
        }

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

        public static void WriteLineToConsole(string text, ConsoleColor color = ConsoleColor.White) {
            Console.ForegroundColor = color;
            Console.WriteLine(text);
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static void WriteToConsole(string text, ConsoleColor color = ConsoleColor.White) {
            Console.ForegroundColor = color;
            Console.Write(text);
            Console.ForegroundColor = ConsoleColor.White;
        }

        public static string IndentText(string text, int amount) {
            return $"{text}".PadLeft(text.Length + (2*amount));

        }
        // Show plan and sub-plans to string
        public static string PlanToString(Plan plan, bool showExpansionState = false) {
            var expansionStateString = showExpansionState ? $"{EnumToDescription(plan.state)}" : "";
            string planText = IndentText($"- {plan.description} ({expansionStateString})\n", plan.planLevel);

            if (plan.subPlans.Any()) {
                foreach (var subPlan in plan.subPlans) {
                    planText += PlanToString(subPlan);
                }
            }
            return planText;
        }

        // Show plan step and parents ABOVE plan
        public static string ReversePlanToString(Plan plan) {
            string planText = IndentText($"- {plan.description}", plan.planLevel)+"\n";

            if (plan.parent != null) {
                planText = $"{ReversePlanToString(plan.parent)}{planText}";
            }
            return planText;
        }

        // Returns plan description for next peer in list
        public static string RemainingPlanToString(Plan plan) 
        {
            var response = "";
            var curPlan = plan;
            var planParent = plan.parent;
            while (planParent != null)
            {
                var index = planParent.subPlans.IndexOf(curPlan)+1;
                for (int i = index; i<planParent.subPlans.Count;i++) {
                    response += IndentText($"- {planParent.subPlans.ElementAt(i).description}", planParent.planLevel+1)+"\n";
                }
                curPlan = planParent;
                planParent = planParent.parent;
            }
            return response;
        }

        public static string PlanToJSON(Plan plan) {
            var json = Newtonsoft.Json.JsonConvert.SerializeObject(plan);
            return json;
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
            Util.WriteLineToConsole(planName, ConsoleColor.Green);
            Util.WriteLineToConsole(configuration.ToString(), ConsoleColor.Green);
            Util.WriteLineToConsole(runData, ConsoleColor.Green);
            Util.WriteLineToConsole(planString, ConsoleColor.White);
        }

        public static String SavePlanAsText(Plan plan, RuntimeConfig configuration, string runData = "") {
            var saveName = GetSaveName(plan, TEXT_FILE_EXTENSION);
            var planString = $"{configuration.ToString()}\n{runData}\n\n";
            planString += PlanToString(plan);
            SaveText(saveName, planString, TEXT_FILE_EXTENSION);
            return saveName;
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

        public static string LoadText(string filename, string extension = TEXT_FILE_EXTENSION) {
            var fileName = $"{SAVE_DIRECTORY}/{filename}.{TEXT_FILE_EXTENSION}";
            if (File.Exists(fileName)) {
                var text = File.ReadAllText(fileName);
                return text;
            }
            return "";
        }

        public static void DisplayProgress(Plan? basePlan, RuntimeConfig configuration, SemaphoreSlim GPTSemaphore, bool detailed = false) {
            if (basePlan is null) return;
            WriteLineToConsole($"\n\nProgress ({configuration.maxConcurrentGPTRequests - GPTSemaphore.CurrentCount} GPT requests in flight):",ConsoleColor.Blue);
            WriteLineToConsole(PlanToString(basePlan, showExpansionState: true), ConsoleColor.Cyan);
        }

        public static string EnumToDescription<T>(this T enumerationValue)
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

        // More efficient way of counting words than split
        static public int NumWordsIn(string text) {
            int wordCount = 0, index = 0;

            // skip whitespace until first word
            while (index < text.Length && char.IsWhiteSpace(text[index]))
                index++;

            while (index < text.Length)
            {
                // check if current char is part of a word
                while (index < text.Length && !char.IsWhiteSpace(text[index]))
                    index++;

                wordCount++;

                // skip whitespace until next word
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;
            }
            return wordCount;
        }

        static public string LimitWordCountTo(string text, int number) {
            return text.Split(' ').Take(number).Aggregate((a, b) => a + " " + b); 
        }

        static public string GetUserInput(string userPrompt = "") {
            if (userPrompt != "") {
                WriteLineToConsole(userPrompt);
            }
            Util.WriteToConsole("> ");
            var userInput = Console.ReadLine();
            return userInput;
        }

        // Show task above and below where looking for expansion
        /*
            - build a house in MineCraft
            - build a foundation for the house
                - place flocks for the foundation
                    <SELECT BEST OPTION HERE>
                - fill in the foundation
            - build wall for the house
            - build a roof for the house
            - decorate the house
        */
        static public string MakeTaskPrompt(Plan plan, string optionPrompt) {
            var taskPrompt = Util.ReversePlanToString(plan);
            taskPrompt+= Util.IndentText($"  {optionPrompt}\n", plan.planLevel + 1);
            taskPrompt += Util.RemainingPlanToString(plan);
            return taskPrompt;
        }

        // Convert 1-based text index into 0-based integer index
        static public int StringToIndex(string text, int maxIndex) {
            int index = 0;
            if (!int.TryParse(text, out index) || index < 1 || index > maxIndex) {
                return -1;  
            }
            else {
                // text is 1-indexed, but list is 0-indexed
                return index-1;
            }
        }

        // Assume response is of the format:
        /*
        <OPTION 7>
        <EXPLAIN YOUR REASONING>
        <OPTION 1> is BAD because Is too specific
        <OPTION 2> is BAD because Is too specific
        <OPTION 3> is BAD because Is too specific
        <OPTION 4> is GOOD because Is specific enough
        */
        static public int ParseAndSetReasoningResponse(string text, List<TaskList> candidateSubTasks) {
            var lines = text.Split('\n');

            // Get best index
            var optionLine = lines[0];
            var bestOption = GetOptionNumber(optionLine, candidateSubTasks.Count);

            // Get index of line that that matches REASONING_PROMPT
            var reasoningLine = Array.IndexOf(lines, BestTaskListExamples.REASONING_PROMPT);
            if (reasoningLine == -1) {
                 // TODO: do something here
                return -1;
            }

            // Skip to reasoning lines
            lines = lines.Skip(reasoningLine+1).ToArray();

            // Extract reasoning and assign to task
            for (int i=0; i < lines.Count(); i++) {
                var optionIndex = GetOptionNumber(lines[i], candidateSubTasks.Count);
                var taskList = candidateSubTasks[optionIndex];
                taskList.reason = lines[i].Split('>')[1].Trim();

                // TODO: Parse/set sentiment
            }

            return bestOption;
        }

        // Extract number (n) from option texts of the for "<OPTION n>"
        static public int GetOptionNumber(string text, int maxIndex) {
            var regex = new Regex(@"<OPTION (\d+)>", RegexOptions.IgnoreCase);
            var match = regex.Match(text);
            if (match.Success) {
                return StringToIndex(match.Groups[1].Value, maxIndex);
            }
            return -1;
        }
    }
}
