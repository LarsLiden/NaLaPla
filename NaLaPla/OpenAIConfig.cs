namespace NaLaPla
{
public class OpenAIConfig {

    public static int DefaultMaxTokens = 500;

        public static int DefaultNumResponses = 4;

        public int MaxTokens;

        // If >1 will produce multiple candidates and do an additional
        // call to GPT to vote for best candidate.  Additional experimentation
        // required to see how much value this adds
        public int NumResponses;

        public float Temperature;

        public OpenAIConfig() {
            this.MaxTokens = DefaultMaxTokens;
            this.NumResponses = DefaultNumResponses;
            this.Temperature = RuntimeConfig.settings.gptTemperature; 
        }

        public OpenAIConfig(int maxTokens, int numResponses, float temperature) {
            this.MaxTokens = maxTokens;
            this.NumResponses = numResponses;
            this.Temperature = temperature;
        }

    }
}