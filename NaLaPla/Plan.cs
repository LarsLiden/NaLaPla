namespace NaLaPla
{
    public class Plan {
        public string? description;
        public int planLevel;        
        public List<Plan>? planSteps;

        public Plan? parent;
    }
}