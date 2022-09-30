namespace NaLaPla
{
    public class Cells
    {
        public List<string> text { get; set; }
        public List<List<double>> bboxes { get; set; }
    }

    public class Headers
    {
        public List<string> text { get; set; }
        public List<List<double>> bboxes { get; set; }
    }

    public class Image
    {
        public string src { get; set; }
        public string path { get; set; }
        public string alt_text { get; set; }
        public object caption { get; set; }
        public List<double> bbox { get; set; }
    }

    public class Metadata
    {
        public string url { get; set; }
        public string title { get; set; }
        public string time { get; set; }
    }

    public class WikiRecord
    {
        public Metadata metadata { get; set; }
        public List<Table> tables { get; set; }
        public List<Image> images { get; set; }
        public List<Sprite> sprites { get; set; }
        public List<Text> texts { get; set; }

        public string Title 
        {
            get {
                return metadata.title.Split("/").Last().Replace("_", " ").Replace("%27","'");
            }
        }
    }

    public class Sprite
    {
        public string text { get; set; }
        public List<double> bbox { get; set; }
    }

    public class Table
    {
        public List<double> bbox { get; set; }
        public Headers headers { get; set; }
        public Cells cells { get; set; }
    }

    public class Text
    {
        public string text { get; set; }
        public List<double> bbox { get; set; }

        public double LeftMargin 
        {
            get {
                return bbox[0];
            }
        }
    }
}