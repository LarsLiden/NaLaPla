namespace NaLaPla
{
    using System;
    using System.IO;
    using System.Text.RegularExpressions;
    using Newtonsoft.Json;

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
            var json = JsonConvert.SerializeObject(plan);
            return json;
        }        

        public static Plan LoadPlan(string planName) {  
            var fileName = $"{planName}.{PLAN_FILE_EXTENSION}";
            var planString = File.ReadAllText($"{SAVE_DIRECTORY}/{fileName}");
            var plan = JsonConvert.DeserializeObject<Plan>(planString);
            if (plan is null) {
                throw new Exception("Null plan loaded");
            }
            return plan;
        }

        public static string GetPlanName(Plan plan) {
            
            var planName = (plan.description is null) ? "--unknown--" : plan.description;
            foreach (var c in Path.GetInvalidFileNameChars()) {
                planName.Replace(c.ToString(),"-");
            }
            return planName;
        }

        private static string GetSaveName(Plan plan, string fileExtension) {
            
            var planName = GetPlanName(plan);
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

        public static void PrintPlanToConsole(Plan plan, string runData="") {
            var planName = GetPlanName(plan);
            var planString = plan.PlanToString();
            Util.WriteLineToConsole(planName, ConsoleColor.Green);
            Util.WriteLineToConsole(RuntimeConfig.settings.ToString(), ConsoleColor.Green);
            Util.WriteLineToConsole(runData, ConsoleColor.Green);
            Util.WriteLineToConsole(planString, ConsoleColor.White);
        }

        public static String SavePlanAsText(Plan plan, string runData = "") {
            var saveName = GetSaveName(plan, TEXT_FILE_EXTENSION);
            var planString = $"{RuntimeConfig.settings.ToString()}\n{runData}\n\n";
            planString += plan.PlanToString();
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
            var fileName = $"{SAVE_DIRECTORY}/{filename}.{extension}";
            if (File.Exists(fileName)) {
                var text = File.ReadAllText(fileName);
                return text;
            }
            return "";
        }

        public static void DisplayProgress(Plan? plan, SemaphoreSlim GPTSemaphore, bool detailed = false) {
            if (plan is null) return;
            WriteLineToConsole($"\n\nProgress ({RuntimeConfig.settings.maxConcurrentGPTRequests - GPTSemaphore.CurrentCount} GPT requests in flight):",ConsoleColor.Blue);
            WriteLineToConsole(plan.PlanToString(showExpansionState: true), ConsoleColor.Cyan);
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
            return userInput ?? "";
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
            if (RuntimeConfig.settings.bestTaskPrompt == BestTaskPromptType.FULL) {
                return MakeFullTaskPrompt(plan, optionPrompt);
            }
            else {
                return MakePartialTaskPrompt(plan, optionPrompt);
            }
        }

        // Show entire task where looking for expansion
        static public string MakeFullTaskPrompt(Plan plan, string optionPrompt) {

            // If plat is root it will not a root
            if (plan.root == null) {
                return plan.PlanToString(null, optionPrompt);
            }
            return plan.root.PlanToString(plan, optionPrompt);
        }

        // Show task above and below where looking for expansion
        static public string MakePartialTaskPrompt(Plan plan, string optionPrompt) 
        {
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
        <OPTION 3> is the best option because it is the most specific and provides the most detail)
        <OPTION 1> is the second best option because it is specific but doesn't provide as much detail as option 1)
        <OPTION 2> is the third best option because it is too general and doesn't provide enough detail)
        */
        static public void ParseReasoningResponse(string text, List<TaskList> candidateSubTasks) {
            var lines = text.Split('\n');
            for (int i = 0; i <lines.Length; i++) {
                var line = lines[i];
                var optionIndex = GetOptionNumber(line, candidateSubTasks.Count);   

                // Skip lines that don't have an option number
                if (optionIndex == -1) {
                    continue;
                }
                
                var reason = line.Substring(line.IndexOf(">")+1).Trim();

                var taskList = candidateSubTasks[optionIndex];
                taskList.reason = reason;
                taskList.ranking = i;
            }
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

        static public T? CopyObject <T>(T source) {
            var serialized = JsonConvert.SerializeObject(source);
            return JsonConvert.DeserializeObject<T>(serialized);
        }
    }
}
