using System.ComponentModel;

namespace NaLaPla
{
    using Lucene.Net.Documents;
    using Newtonsoft.Json.Linq;
    using HtmlAgilityPack;

    class MineCraftDataProvider : IDataProvider
    {
    
        private string MineCraftDataFolder = "";

        // Number of words to require before adding section to index
        const int MIN_REQUIRED_WORDS = 5;

        const string SOURCE_DIR = "GroundingData";

        // Stack of files to process
        private Stack<string> _DataFiles;

        // Queue of document section w/in file to process
        private Queue<string> SubSections = new Queue<string>();

        // Current loaded file
        private WikiRecord CurWikiRecord;
    

        private string DataPath {
            get {
                return Path.Combine(Environment.CurrentDirectory, SOURCE_DIR, MineCraftDataFolder);
            }
        }

        public MineCraftDataProvider(string mineCraftDataFolder) {
            this.MineCraftDataFolder = mineCraftDataFolder;

            if(!System.IO.Directory.Exists(DataPath)) {
                throw new Exception($"Directory Not Found {DataPath}");
            }
        }

        // Utility that removes image directories from the wiki dataset as they aren't needed
        private void CleanFiles() {
            var dirNames = new Stack<string>(System.IO.Directory.GetDirectories(DataPath, "images", SearchOption.AllDirectories));
            foreach (var dirName in dirNames) {
                System.IO.Directory.Delete(dirName, true);
                Util.WriteLineToConsole($"Deleting: {dirName}", ConsoleColor.DarkGreen);

            }
            Util.WriteLineToConsole($"Deleted {dirNames.Count} directories", ConsoleColor.Green );
        }

        public static void UtilCopyOnlyJSONFiles(string sourceDir, string destDir) {
            var sourceFolder = Path.Combine(Environment.CurrentDirectory, SOURCE_DIR, sourceDir); 
            var destFolder = Path.Combine(Environment.CurrentDirectory, SOURCE_DIR, destDir); 
            CopyOnlyJSONFiles(sourceFolder, destFolder);
        }
        private static void CopyOnlyJSONFiles(string sourceDir, string destDir) {

            var fileNames = new Stack<string>(System.IO.Directory.GetFiles(sourceDir, "data.json"));
            
            if (!System.IO.Directory.Exists(destDir)) {
                System.IO.Directory.CreateDirectory(destDir);
            }

            foreach (var f in fileNames) {
                string fileName = f.Substring(sourceDir.Length + 1);
                File.Copy(Path.Combine(sourceDir, fileName), Path.Combine(destDir, fileName), true);
            }

            var dirNames = new Stack<string>(System.IO.Directory.GetDirectories(sourceDir));
            foreach (var d in dirNames) {
                string dirName = d.Substring(sourceDir.Length +1);
                CopyOnlyJSONFiles(Path.Combine(sourceDir, dirName), Path.Combine(destDir, dirName));
            }
        }


        private Stack<string> DataFiles {
            get {
                if (_DataFiles == null) {
                    _DataFiles = new Stack<string>(System.IO.Directory.GetFiles(DataPath, "data.json", SearchOption.AllDirectories));
                    Util.WriteLineToConsole($"{_DataFiles.Count()} Documents Found", ConsoleColor.Green);
                }
                return _DataFiles;
            }
        }

        // Clean up the inner text
        private string CleanInnerText(string text) {
            return text.Replace("[]","").Replace("\n", "").Replace("\t", "");
        }

        // Augment wikiRecord with given tag type
        private void SetTags(WikiRecord wikiRecord, HtmlDocument doc, string tag) {
            // Gather list of inner text
            var texts = new List<string>();
            var nodes = doc.DocumentNode.SelectNodes($"//{tag}");
            if (nodes == null) {
                return;
            }

            foreach (HtmlNode node in nodes) {
                texts.Add(CleanInnerText(node.InnerText));
            }

            // Create a dictionary with count of occurances
            Dictionary<string, int> tagDictionary = new Dictionary<string, int>();
            foreach (var text in texts) {
                if (tagDictionary.ContainsKey(text)) {
                    tagDictionary[text]++;
                }
                else {
                    tagDictionary[text] = 1;
                }
            }

            // Add html tag if number of occurrences matches
            foreach (var keyValue in tagDictionary) {
                var textObjs = wikiRecord.texts.Where(t => t.text == keyValue.Key);
                if (textObjs.Count() == keyValue.Value) {
                    foreach (var textObj in textObjs) {
                        textObj.htmlTag = tag;
                    }
                }
            }
        }
        private void AugmentWithDomData(WikiRecord wikiRecord) {

            try {
                // If already augmented don't need to do again
                if (wikiRecord.metadata.hasBeenAugmented) {
                    return;
                }
                Util.WriteLineToConsole($"Augmenting Data: {wikiRecord.Title}", ConsoleColor.Gray);
                var web = new HtmlWeb();
                var doc = web.Load(wikiRecord.metadata.url);
                
                // Add tag information to wiki record
                SetTags(wikiRecord, doc, "h1");
                SetTags(wikiRecord, doc, "h2");
                SetTags(wikiRecord, doc, "h3");
                SetTags(wikiRecord, doc, "p");
                wikiRecord.metadata.hasBeenAugmented = true;

                // Re-save file with added tags
                var json = Newtonsoft.Json.JsonConvert.SerializeObject(wikiRecord);
                var writer = new StreamWriter(wikiRecord.metadata.fileName);
                writer.Write(json);
                writer.Close();
            }
            catch (Exception e) {
                Util.WriteLineToConsole($"Augmentation error {e.ToString()}");
                throw e;
            }
        }

        // Get next document section.  If all sections have been handled, move on to next file
        private string? NextDocumentSection() {
            if (SubSections.Count == 0) {
                if (DataFiles.Count == 0) {
                    return null;
                }
                var nextFile = DataFiles.Pop();
                var jsonString = File.ReadAllText(nextFile);
                CurWikiRecord = Newtonsoft.Json.JsonConvert.DeserializeObject<WikiRecord>(jsonString);
                CurWikiRecord.metadata.fileName = nextFile;
                AugmentWithDomData(CurWikiRecord);

                if (CurWikiRecord == null || CurWikiRecord.texts == null) {
                    return NextDocumentSection();    
                }
                var subTexts = CurWikiRecord.texts.Select(t => t.text);
                SubSections = GenerateDocumentSections(CurWikiRecord);
                Util.WriteLineToConsole($"Adding: {CurWikiRecord.Title, -30} with {SubSections.Count, -10} items ({DataFiles.Count()} remaining)", ConsoleColor.DarkGreen);
                return NextDocumentSection();
            } 
            var nextDocument = SubSections.Dequeue();    
            return nextDocument;
        }

        // Capture data from tables that involves steps and descriptions
        private void ProcessTable(Table curTable, Queue<string> outputQueue) {
            var stepCol = curTable.headers.text.IndexOf("Step");
            var descCol = curTable.headers.text.IndexOf("Description");
            if (stepCol < 0 || descCol < 0) {
                return;
            }

            var section = "";
            for (int i=0;i<curTable.cells.text.Count; i=i+curTable.headers.text.Count) {
                section += $"{curTable.cells.text[i+stepCol]}. {curTable.cells.text[i+descCol]}\n";
            }
            outputQueue.Enqueue(section);
        }

        private int TagLevel(Text text) {
            if (text.htmlTag == "h1") {
                return 1;
            }
            if (text.htmlTag == "h2") {
                return 2;
            }
            if (text.htmlTag == "h3") {
                return 3;
            }
            return 4;
        }

        private String? GetBodyText(List<Text> textItems) {

            // Filter out contents box
            if (textItems[1] != null && textItems[1].text == "Contents") {
                return null;
            }

            // Filter out section with no body
            if (TagLevel(textItems.Last()) < 4) {
                return null;
            }

            string output = "";
            foreach (var text in textItems) {
                output += $"{text.text}\n";
            }
            return output;
        }

        // Process text into sections by headers
        private void ProcessText(Queue<Text> inputQueue, Queue<string> outputQueue, List<Text> textItems) {

            var lastTagLevel = textItems.Any() ? TagLevel(textItems.Last()) : 0;

            // If no next item
            if (!inputQueue.TryDequeue(out Text curText)) {
                // Add to output if more than just headers
                if (lastTagLevel > 3) {
                    var bodyText = GetBodyText(textItems);
                    if (bodyText != null) {
                        outputQueue.Enqueue(bodyText);    
                    }
                }
                return;
            }

            var curTagLevel = TagLevel(curText);

            // If moved up a tag level move to next item
            if (curTagLevel < lastTagLevel) {
                var bodyText = GetBodyText(textItems);
                if (bodyText != null) {
                    outputQueue.Enqueue(bodyText);
                }

                // Trim at current tag level and add cur text
                textItems = textItems.Take(curTagLevel-1).ToList();
                textItems.Add(curText);
                ProcessText(inputQueue, outputQueue, textItems);
            }
            // If moved down a text level add it
            else if (curTagLevel > lastTagLevel) {
                textItems.Add(curText);
                ProcessText(inputQueue, outputQueue, textItems);
            }
            // If same text level
            else if (curTagLevel == lastTagLevel) {
                // If a repeated header, replace it with the new one
                if (curTagLevel < 4) {
                    textItems.RemoveAt(lastTagLevel - 1);
                }
                textItems.Add(curText);
                ProcessText(inputQueue, outputQueue, textItems);
            }
        }
        
        // Filter on document section
        private Queue<string> GenerateDocumentSections(WikiRecord wikiRecord) {
            var outputQueue = new Queue<string>();

            // Add text sections
            var inputQueue = new Queue<Text>(wikiRecord.texts);
            ProcessText(inputQueue, outputQueue, new List<Text>());

            // Add table sections
            var tableQueue = new Queue<Table>(wikiRecord.tables);
            while (tableQueue.TryDequeue(out Table nextTable)) {
                ProcessTable(nextTable, outputQueue); 
            }             

            return outputQueue;
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