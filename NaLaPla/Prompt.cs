namespace NaLaPla
{
public class Prompt {
        public string text = "";

        public OpenAIConfig OAIConfig = new OpenAIConfig();

        public Prompt(string text="") {
            this.text = text;
        }

        public override string ToString()
        {
            return text;
        }

    }
}