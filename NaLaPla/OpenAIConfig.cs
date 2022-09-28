namespace NaLaPla
{
public class OpenAIConfig {
        public int MaxTokens = 500;

        // If >1 will produce multiple candidates and do an additional
        // call to GPT to vote for best candidate.  Additional experimentation
        // required to see how much value this adds
        public int NumResponses = 3;

        public float Temperature =  0.2f;
    }
}