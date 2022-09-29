using System.Reflection;
namespace NaLaPla
{
public class Prompt {
        public string text = "";
        
        public List<Response> responses = new List<Response>();

        public OpenAIConfig OAIConfig = new OpenAIConfig();

        public Prompt(string text,RuntimeConfig? configuration) {
            this.text = text;
            if (configuration is not null) {
                this.OAIConfig.Temperature = configuration.temperature;
            }
        }

        public override string ToString()
        {
            return text;
        }

    }
}