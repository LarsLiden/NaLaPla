namespace NaLaPla
{
    public enum ExpandModeType
    {
        // Expand sub-tasks one at a time
        ONE_BY_ONE,

        // Expand all sub-tasks at the same time
        AS_A_LIST    
    }

       public enum BestResponseChooserType
    {
        // User picks best response when GPT provides multiple possibilities
        USER,

        // GPT automaticaly picks best response when GPT provides multiple possibilities
        GPT    
    }

    public class RuntimeConfig {

        public ExpandModeType ExpandMode = ExpandModeType.AS_A_LIST;

        public int maxConcurrentGPTRequests = 1;

        // Should grounding be added to prompts
        public bool useGrounding = false;

        public int expandDepth = 2;
        public float temperature = 0.1f;
        public float tempMultPerLevel = 1.0f;

        // Number of subtasks to request in prompt
        public string promptSubtaskCount = "";  // Default to not specifying number of sub-tasks.  Creates less noise

        public bool shouldLoadPlan = false;

        // When GPT provides more than one response, who chooses the best one?
        public BestResponseChooserType bestResponseChooser = BestResponseChooserType.USER;

        public string indexToBuild = "";

        public DisplayOptions displayOptions = new DisplayOptions();

        public override string ToString() {
            var stringified = $"expandDepth = {expandDepth}, subtaskCount = {promptSubtaskCount},"
            + $" default temperature = {temperature}, temperature multiplier per level = {tempMultPerLevel}, maxConcurrentGPTRequests = {maxConcurrentGPTRequests},"
            + $" useGrounding = {useGrounding}, shouldLoadPlan = {shouldLoadPlan}, indexToBuild = {indexToBuild},"
            + $" {displayOptions.ToString()}";
            return stringified;
        }
    }
}