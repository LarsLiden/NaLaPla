namespace NaLaPla
{
    public enum ResponseType {
        GPT3
    }

    public class Response {
        public string text = "";
        public ResponseType responseType;

        public float score = 0.0f;

        public Response(ResponseType responseType, String text) {
            this.responseType = responseType;
            this.text = text;
        }

        public override string ToString()
        {
            return this.text;
        }

    }
}