using System.ComponentModel;

namespace NaLaPla
{
    using Lucene.Net.Documents;
    using Newtonsoft.Json.Linq;

    class MineCraftDataProvider : IDataProvider
    {
    
        const string MINECRAFT_DATA_FOLDER = "wiki_samples";

        const int MIN_REQUIRED_WORDS = 5;

        // Stack of files to process
        private Stack<string> _DataFiles = null;

        // Stock of document section w/in file to process
        private Stack<string> SubSections = new Stack<string>();

        // Current loaded file
        private WikiRecord CurWikiRecord = null;
    
        private Stack<string> DataFiles {
            get {
                if (_DataFiles == null) {
                    var path = Path.Combine(Environment.CurrentDirectory, MINECRAFT_DATA_FOLDER);
                    _DataFiles = new Stack<string>(System.IO.Directory.GetFiles(path, "data.json", SearchOption.AllDirectories));
                    Util.WriteToConsole($"{_DataFiles.Count()} Documents Found", ConsoleColor.Green);
                }
                return _DataFiles;
            }
        }

        // Get next document section.  If all sections have been handles, move on to next file
        private string NextDocumentSection() {
            if (SubSections.Count == 0) {
                if (DataFiles.Count == 0) {
                    return null;
                }
                var nextFile = DataFiles.Pop();
                var jsonString = File.ReadAllText(nextFile);
                CurWikiRecord = Newtonsoft.Json.JsonConvert.DeserializeObject<WikiRecord>(jsonString);
                var subTexts = CurWikiRecord.texts.Select(t => t.text);
                SubSections = GetValidDocuments(subTexts);
                Util.WriteToConsole($"Adding: {CurWikiRecord.Title, -30} ({DataFiles.Count()} remaining)", ConsoleColor.DarkGreen);
            }
            var nextDocument = SubSections.Pop();    
            return nextDocument;
        }

        // Filter on document section
        private Stack<string> GetValidDocuments(IEnumerable<string> documents) {
            var validDocuments = documents.ToList().Where(d => {
                // Ignore short document sections as they are mostly titles, section headings, etc
                // TODO: Might be able do something smart here and combine document sections
                var numWords = Util.NumWordsIn(d);
                return numWords > MIN_REQUIRED_WORDS;
            });
            return new Stack<string>(validDocuments);
        }

        public Document GetNextDocument() {
            
            var documentBody = NextDocumentSection();
            if (documentBody == null) {
                return null;
            }                
            var doc = new Document
            {
                // StringField indexes but doesn't tokenize
                new StringField("topic", CurWikiRecord.Title, Field.Store.YES),
                new TextField("body", $"{CurWikiRecord.Title} - {documentBody}", Field.Store.YES)
            };
            return doc;
        }
    }
}