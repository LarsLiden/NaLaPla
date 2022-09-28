using System.Reflection;
namespace NaLaPla
{
public class Prompt {
        public string text = "";

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