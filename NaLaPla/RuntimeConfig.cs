namespace NaLaPla
{
    using System.ComponentModel;
    using System.Reflection;
    using Newtonsoft.Json;

    public enum ExpandModeType
    {
        // Expand sub-tasks one at a time
        ONE_BY_ONE,

        // Expand all sub-tasks at the same time
        AS_A_LIST    
    }

    public enum ResponseChooserType
    {
        // User picks best response when GPT provides multiple possibilities
        USER,

        // GPT automatically picks best response when GPT provides multiple possibilities
        GPT    
    }

    public class RuntimeConfig {

        private static RuntimeConfig? _instance = null;
        public static RuntimeConfig settings { 
            get {
                if (_instance == null) {

                    // Load settings from disk
                    var json = Util.LoadText("", "Settings", Util.JSON_FILE_EXTENSION);
                    if (json != null) {
                        _instance = JsonConvert.DeserializeObject<RuntimeConfig>(json);
                    }
                    if (_instance == null) {
                        _instance = new RuntimeConfig();
                    }

                    // Create lookup table for CLI
                    var index = 0; 
                    var properties = typeof(RuntimeConfig).GetProperties();
                    RuntimeConfig.indexToProperty = new Dictionary<int, string>();
                    foreach (var property in properties) {
                        var description = property.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
                        if (description.Any()) {
                            RuntimeConfig.indexToProperty.Add(index, property.Name);
                            index++;
                        }
                    }
                }
                return _instance;
            }
        } 

        public static Dictionary<int, string> indexToProperty { get; set;} = new Dictionary<int, string>();

        // NOTE:
        //
        // Do not change these default value.  Instead use Setting.json or the CLI to change them.
        //
        [Description("Maximum number of concurrent GPS requests")]
        public int gptMaxConcurrentRequests { get; set;} = 1;

        [Description("Default Temperature for GPT")]
        public float gptTemperature { get; set;} = 0.1f;

        [Description("If less than one, decay temperature by this fraction each time a plan is expanded")]
        public float gptTemperatureDecay { get; set;} = 1.0f;

        [Description("When prompting for the best task.  PARTIAL: Use partial plan, FULL: Use full plan")]
        public BestTaskPromptType promptBestTask { get; set;} = BestTaskPromptType.PARTIAL;

        [Description("Should grounding data be added to prompts")]
        public bool promptUseGrounding { get; set;} = false;
    
        [Description("Number of subtasks to request in prompt")]
        public string promptSubtaskCount { get; set;} = "";  // Default to not specifying number of sub-tasks.  Creates less noise

        [Description("Should plans cached on disk be used")]
        public bool expandUseCachedPlans { get; set;} = false;

        [Description("How deep should plans be expanded")]
        public int expandDepth { get; set;} = 2;

        [Description("When GPT provides more than one response, who chooses the best one?  USER: User chooses, GPT: GPT chooses automatically")]
        public ResponseChooserType expandResponseChooser { get; set;} = ResponseChooserType.USER;

        [Description("Strategy to use for expanding plans.  AS_A_LIST: Expand all sub-tasks at the same time, ONE_BY_ONE: Expand sub-tasks one at a time")]
        public ExpandModeType expandMode { get; set;} = ExpandModeType.AS_A_LIST;

        [Description("Show plan progress at every step")]
        public bool showProgress { get; set;} = false;

        [Description("Show each prompt as it is submitted to GPT.")]
        public bool showPrompts { get; set;} = false; 
        
        [Description("Show the parsed result of each request")]
        public bool showResults { get; set;} = false; 

        [Description("Show document retrieval when grounding is employed")]
        public bool showGrounding = false;

        // Lookup property by name or index
        public PropertyInfo? PropertyByNameOrIndex(string settingName) {

            var propertyName = settingName.ToLower();

            // Check if settingName is a number
            if (int.TryParse(settingName, out var index)) {
                if (RuntimeConfig.indexToProperty.ContainsKey(index)) {
                    propertyName = indexToProperty[index];
                }
            }
            
            var property = typeof(RuntimeConfig).GetProperty(propertyName, BindingFlags.IgnoreCase |  BindingFlags.Public | BindingFlags.Instance);
            return property;
        }

        public void UpdateSetting(PropertyInfo property, string value) {

            if (property.PropertyType == typeof(bool)) {
                if (bool.TryParse(value, out var boolValue)) {
                    property.SetValue(RuntimeConfig.settings, boolValue);
                }
                else {
                    Util.WriteLineToConsole($"Invalid boolean value {value} for {property.Name}");
                }
            }
            else if (property.PropertyType == typeof(int)) {
                if (int.TryParse(value, out var intValue)) {
                    property.SetValue(RuntimeConfig.settings, intValue);
                }
                else {
                    Util.WriteLineToConsole($"Invalid int value {value} for {property.Name}");
                }
            }
            else if (property.PropertyType == typeof(float)) {
                if (float.TryParse(value, out var floatValue)) {
                    property.SetValue(RuntimeConfig.settings, floatValue);
                }
                else {
                    Util.WriteLineToConsole($"Invalid float value {value} for {property.Name}");
                }
            }
            else if (property.PropertyType == typeof(ExpandModeType)) {
                if (Enum.TryParse(value, out ExpandModeType expandModeType)) {
                    property.SetValue(RuntimeConfig.settings, expandModeType);
                }
                else {
                    Util.WriteLineToConsole($"Invalid ExpandModeType value {value} for {property.Name}");
                }
            }
            else if (property.PropertyType == typeof(ResponseChooserType)) {
                if (Enum.TryParse(value, out ResponseChooserType expandModeType)) {
                    property.SetValue(RuntimeConfig.settings, expandModeType);
                }
                else {
                    Util.WriteLineToConsole($"Invalid BestResponseChooserType value {value} for {property.Name}");
                }
            }
            else if (property.PropertyType == typeof(BestTaskPromptType)) {
                if (Enum.TryParse(value, out BestTaskPromptType expandModeType)) {
                    property.SetValue(RuntimeConfig.settings, expandModeType);
                }
                else {
                    Util.WriteLineToConsole($"Invalid BestTaskPromptType value {value} for {property.Name}");
                }
            }
            else if (property.PropertyType == typeof(string)) {
                property.SetValue(RuntimeConfig.settings, value);
            }
            else {
                Util.WriteLineToConsole($"Unknown type {property.GetType()} for {property.Name}");
            }
        }
        public void ShowSettings() {
            var properties = typeof(RuntimeConfig).GetProperties();

            foreach (var property in properties) {
                ShowSetting(property);
            }
        }

        public void ShowSetting(PropertyInfo property) {
            var value = property.GetValue(this);
            var description = property.GetCustomAttributes(typeof(System.ComponentModel.DescriptionAttribute), false);
            if (description.Any()) {
                var descriptionAttribute = description.First() as System.ComponentModel.DescriptionAttribute;
                if (descriptionAttribute != null) {
                    var index = indexToProperty.FirstOrDefault(x => x.Value == property.Name).Key;
                    Util.WriteLineToConsole($"{index,2}) {property.Name,-25} = {value, -20} {descriptionAttribute.Description}", ConsoleColor.Cyan);
                }
            }
        }

        public void Save() {
            var json = JsonConvert.SerializeObject(this);
            Util.SaveText("", "Settings", Util.JSON_FILE_EXTENSION, json);
        }
    }
}