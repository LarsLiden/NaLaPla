using System.ComponentModel;

namespace NaLaPla
{
public enum TaskState {
    [Description("Created")]
    CREATED,
    [Description("Processing")]
    PROCESSING,
    [Description("GPT Request submitted")]
    GPT_PROMPT_SUBMITTED,
    [Description("GPT Response received")]
    GPT_RESPONSE_RECEIVED,
    [Description("Final leaf")]
    FINAL,
    [Description("Done")]
    DONE
}

public class Task {
        public string? description;
        public int planLevel;    
        public List<string> subTaskDescriptions = new List<string>();    
        public List<Task> subTasks = new List<Task>();
        public Task? parent;
        public TaskState state = TaskState.CREATED;
        public string? prompt;
        public string? GPTresponse;
    }
}