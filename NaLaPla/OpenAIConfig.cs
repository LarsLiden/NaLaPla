namespace NaLaPla
{
public class OpenAIConfig {

    public static int DefaultMaxTokens = 500;

        public static int DefaultNumResponses = 3;

        public static float DefaultTemperature =  0.3f;

        public int MaxTokens = DefaultMaxTokens;

        // If >1 will produce multiple candidates and do an additional
        // call to GPT to vote for best candidate.  Additional experimentation
        // required to see how much value this adds
        public int NumResponses = DefaultNumResponses;

        public float Temperature = DefaultTemperature;

        public OpenAIConfig() {}

        public OpenAIConfig(int maxTokens, int numResponses, float temperature) {
            this.MaxTokens = maxTokens;
            this.NumResponses = numResponses;
            this.Temperature = temperature;
        }

    }
}