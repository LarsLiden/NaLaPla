namespace NaLaPla
{
public class Task {
        public string? description;
        public int planLevel;    

        public List<string> subTaskDescriptions = new List<string>();    
        public List<Task> subTasks = new List<Task>();

        public Task? parent;
    }
}