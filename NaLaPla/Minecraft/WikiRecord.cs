namespace NaLaPla
{
    public class Cells
    {
        public List<string> text { get; set; } = new List<string>();
        public List<List<double>> bboxes { get; set; } = new List<List<double>>();
    }

    public class Headers
    {
        public List<string> text { get; set; } = new List<string>();
        public List<List<double>> bboxes { get; set; } = new List<List<double>>();
    }

    public class Image
    {
        public string src { get; set; } = "";
        public string path { get; set; } = "";
        public string alt_text { get; set; } = "";
        public object? caption { get; set; }
        public List<double> bbox { get; set; } = new List<double>();
    }

    public class Metadata
    {
        public string url { get; set; } = "";
        public string title { get; set; } = "";
        public string time { get; set; } = "";

        // Has the data been augmented with HTML info
        public bool hasBeenAugmented { get; set; } = false;

        public string fileName { get; set; } = "";
    }

    public class WikiRecord
    {
        public Metadata metadata { get; set; } = new Metadata();
        public List<Table> tables { get; set; } = new List<Table>();
        public List<Image> images { get; set; } = new List<Image>();
        public List<Sprite> sprites { get; set; } = new List<Sprite>();
        public List<Text> texts { get; set; } = new List<Text>();

        public string Title 
        {
            get {
                return metadata.title.Split("/").Last().Replace("_", " ").Replace("%27","'");
            }
        }
    }

    public class Sprite
    {
        public string text { get; set; } = "";
        public List<double> bbox { get; set; } = new List<double>();
    }

    public class Table
    {
        public List<double> bbox { get; set; } = new List<double>();
        public Headers headers { get; set; } = new Headers();
        public Cells cells { get; set; } =  new Cells();
    }

    public class Text
    {
        public string text { get; set; } = "";
        public List<double> bbox { get; set; } = new List<double>();

        public string htmlTag { get; set; } = "";
        public double LeftMargin 
        {
            get {
                return bbox[0];
            }
        }
    }
}