using System.ComponentModel;

namespace NaLaPla
{
    using Lucene.Net.Documents;
    using Newtonsoft.Json.Linq;

    class MineCraftDataProvider : IDataProvider
    {
    
        const string MINECRAFT_DATA_FOLDER = "wiki_samples";

        bool done = false;

        private Stack<string> _DataFiles = null;
    
        private Stack<string> DataFiles {
            get {
                if (_DataFiles == null) {
                    var path = Path.Combine(Environment.CurrentDirectory, MINECRAFT_DATA_FOLDER);
                    _DataFiles = new Stack<string>(System.IO.Directory.GetFiles(path, "data.json", SearchOption.AllDirectories));
                }
                return _DataFiles;
            }
        }

        public Document GetNextDocument() {
            
            if (DataFiles.Count == 0) {
                return null;
            }
            var nextFile = DataFiles.Pop();

            var jsonString = File.ReadAllText(nextFile);
            
            WikiRecord wikiRecord = Newtonsoft.Json.JsonConvert.DeserializeObject<WikiRecord>(jsonString);

            string record = "";
            foreach (var item in wikiRecord.texts) {
                record += item.text+Environment.NewLine;
            }

            Console.WriteLine($"Adding: {wikiRecord.Title}");
            var doc = new Document
            {
                // StringField indexes but doesn't tokenize
                new StringField("topic", wikiRecord.Title, Field.Store.YES),
                new TextField("body", record, Field.Store.YES)
            };
            done = true;
            return doc;
        }
    }
}