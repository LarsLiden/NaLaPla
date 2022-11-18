
using System.Reflection;

namespace NaLaPla
{
    // A plans and the index of TaskList selected as the best response
    public class CommandLineInterface {

        static Plan? rootPlan = null;

        static public async Task GetBasePlanDescription() {

            Util.WriteLineToConsole("-----------------------------------------------");
            Util.WriteLineToConsole("What do you want to plan? (or 'C' for command menu)");
            var userInput = Util.GetUserInput();
            if (userInput == "") {
                await GetBasePlanDescription();
            }
            if (userInput.ToUpper() == "C") {
                await GetCommand();
            }
            else {
                await PlanExpander.ExpandPlan(userInput);
            }
        }

        static public async Task GetCommand() {
            Util.WriteLineToConsole($"L) Load a plan from disk", ConsoleColor.Cyan);
            Util.WriteLineToConsole($"E) Expand a new plan", ConsoleColor.Cyan);
            Util.WriteLineToConsole($"S) Change settings", ConsoleColor.Cyan);
            Util.WriteLineToConsole($"I) Generate Grounding Index", ConsoleColor.Cyan);
            Util.WriteLineToConsole($"Q) Quit", ConsoleColor.Cyan);

            var userInput = Util.GetUserInput().ToUpper();
            if (userInput == "") {
                await GetCommand();
            }
            else if (userInput.StartsWith("L")) {
                var planDescription = Util.GetUserInput($"Plan Description");
                rootPlan = LoadPlan(planDescription);
            }
            else if (userInput == "E") {
                await GetBasePlanDescription();
            }
            else if (userInput == "S") {
                UpdateSettings();
                await GetCommand();
            }
            else if (userInput == "I") {
                BuildGroundingIndex();
            }
            else if (userInput == "Q") {
                Environment.Exit(0);
            }
            else {
                Util.WriteLineToConsole($"Unknown command {userInput}");
                await GetCommand();
            }
        }

        static public void UpdateSettings() {
            RuntimeConfig.settings.ShowSettings();
            var userInput = Util.GetUserInput($"Setting to change, 'P' previous menu, 'S' save setting to disk").ToUpper();
            if (userInput == "P") {
                return;
            }
            if (userInput == "S") {
                RuntimeConfig.settings.Save();
            }
            else {
                var property = RuntimeConfig.settings.PropertyByNameOrIndex(userInput);
                if (property == null) {
                    Util.WriteLineToConsole($"Unknown setting {userInput}");
                    UpdateSettings();
                }
                else {
                    RuntimeConfig.settings.ShowSetting(property);
                    var value = Util.GetUserInput($"New value for {property.Name}");
                    RuntimeConfig.settings.UpdateSetting(property, value);
                    UpdateSettings();
                }
            }
        }

        static public Plan LoadPlan(string planDescription) {
            Console.WriteLine($"Loading {planDescription}");
            return Util.LoadPlan(planDescription); 
        }

        static public void BuildGroundingIndex() {
            // TODO: This is currently MineCraft specific.  Add generic support for different grounding
            var sourceFileName = Util.GetUserInput("Filename of grounding source data");
            try {
                IR.CreateIndex(new MineCraftDataProvider(sourceFileName));
            }
            catch (Exception e) {
                Util.WriteLineToConsole("Failed to create Index", ConsoleColor.Red);
                Util.WriteLineToConsole(e.Message.ToString(), ConsoleColor.DarkRed);
            }
        }
    }
}