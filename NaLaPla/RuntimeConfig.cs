using System.Reflection;

namespace NaLaPla
{
    public enum ExpandModeType
    {
        // Expand sub-tasks one at a time
        ONE_BY_ONE,

        // Expand all sub-tasks at the same time
        AS_A_LIST    
    }

    public class RuntimeConfig {
        public class ConfigVariable {
            public string cmdLine;
            public string method;
            public string description;

            public ConfigVariable(string cmdLine, string method, string description) {
                this.cmdLine = cmdLine;
                this.method = method;
                this.description = description;
            }
        }

        public List<ConfigVariable> configStringToProperty = new List<ConfigVariable>() {
            new ConfigVariable(cmdLine:"DEPTH", method:"expandDepth", description:"Plan hierarchy depth"),
            new ConfigVariable(cmdLine:"TEMP", method:"temperature", description:"Default temperature"),
            new ConfigVariable(cmdLine:"TEMPMULT", method:"tempMultPerLevel", description:"Temperature multiplier per level"),
            new ConfigVariable(cmdLine:"MAXGPT", method:"maxConcurrentGPTRequests", description:"Max GPT requests in flight at once"),
            new ConfigVariable(cmdLine:"SUBTASKS", method:"subtaskCount", description:"Subtask count to ask for, e.g. 'four'"),
            new ConfigVariable(cmdLine:"SHOWGROUND", method:"showGrounding", description:"Show grounding info"),                    
            new ConfigVariable(cmdLine:"USEGROUND", method:"useGrounding", description:"Use grounding info"),                    
            new ConfigVariable(cmdLine:"SHOWPROMPT", method:"showPrompts", description:"Show prompts as they are sent to GPT"),          
            new ConfigVariable(cmdLine:"DEFACTOR", method:"defaultActor", description:"Default actor to use in prompts"),                    
            new ConfigVariable(cmdLine:"DEFCONTEXT", method:"defaultContext", description:"Default context to use in prompts"),          

            // Actions
            new ConfigVariable(cmdLine:"LOAD", method:"shouldLoadPlan", description:"Load a previously-used plan"),
            new ConfigVariable(cmdLine:"INDEX", method:"indexToBuild", description:"Build an index"),
        };              
        Dictionary<string, MethodInfo> setters = new Dictionary<string, MethodInfo>();

        public ExpandModeType ExpandMode {get; set;} = ExpandModeType.ONE_BY_ONE;

        public int maxConcurrentGPTRequests {get; set;} = 1;

        // whether or not to print each prompt as it is submitted to GPT. Prompts always stored in plan.prompt.
        public bool showPrompts {get; set;} = true; 
        
        // print the parsed result of each request to the console
        public bool showResults {get; set;} = true; 

        // Show document retrieval when grounding is employed
        public bool showGrounding {get; set;} = true;

        // Should grounding be added to prompts
        public bool useGrounding {get; set;} = true;

        public int expandDepth {get; set;} = 2;
        public float temperature {get; set;} = 0.1f;
        public float tempMultPerLevel {get; set;} = 1.0f;
        public string subtaskCount {get; set;} = "";  // Default to not specifying number of sub-tasks.  Creates less noise
        public string defaultActor {get; set;} = "a computer agent";
        public string defaultContext {get; set;} = "in Minecraft";
        public bool shouldLoadPlan {get; set;} = false;

        public string indexToBuild {get; set;} = "";

        public override string ToString() {
            var stringified = $"expandDepth = {expandDepth}, subtaskCount = {subtaskCount},"
            + $" default temperature = {temperature}, temperature multiplier per level = {tempMultPerLevel}, maxConcurrentGPTRequests = {maxConcurrentGPTRequests},"
            + $" showPrompts = {showPrompts}, showResults = {showResults}, showGrounding = {showGrounding}, useGrounding = {useGrounding}, shouldLoadPlan = {shouldLoadPlan},"
            + $" indexToBuild = {indexToBuild}, defaultActor = {defaultActor}, defaultContext = {defaultContext}";
            return stringified;
        }

        public bool SaveSettings(string settingsFile) {
            var hack = new List<RuntimeConfig>();
            hack.Add(this);
            var hackWrapper = new {RuntimeConfig = hack};

            string json = Newtonsoft.Json.JsonConvert.SerializeObject(hackWrapper,Newtonsoft.Json.Formatting.Indented);
            System.IO.File.WriteAllText(settingsFile, json);
            return true;
        }

        public void SetValue(string key, string? value) {
            MethodInfo? targetMethod;
            setters.TryGetValue(key, out targetMethod);
            if (targetMethod is not null) {
                Type targetType = targetMethod.GetParameters().First().ParameterType;
                if (value is not null) {
                    var targetValue = Convert.ChangeType(value, targetType);
                    if (targetValue is not null) {
                        targetMethod.Invoke(this, new object[] {targetValue});
                    } else {
                        targetMethod.Invoke(this, new object[] {Type.Missing});
                    }
                }
            }
        }

        public RuntimeConfig() {
            foreach(var item in configStringToProperty) {
                var method = this.GetType().GetProperty(item.method);
                if (method is null) {
                    throw new Exception("Bad config string to param mapping");
                }
                var setterMethod = method.GetSetMethod();
                if (setterMethod is null) {
                    throw new Exception("Bad config string to param mapping");
                }
                setters.Add(item.cmdLine,setterMethod);
            }
        }
    }
}