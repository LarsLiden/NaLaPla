using System.ComponentModel;

namespace NaLaPla
{
    using Lucene.Net.Documents;
    using Newtonsoft.Json.Linq;
    using HtmlAgilityPack;

    class MineCraftDataProvider : IDataProvider
    {
    
        private string MinecraftDataFolder = "";

        // Number of words to require before adding section to index
        const int MIN_REQUIRED_WORDS = 5;

        const string SOURCE_DIR = "GroundingData";

        // Stack of files to process
        private Stack<string> _DataFiles = null;

        // Queue of document section w/in file to process
        private Queue<string> SubSections = new Queue<string>();

        // Current loaded file
        private WikiRecord CurWikiRecord = null;
    

        private string DataPath {
            get {
                return Path.Combine(Environment.CurrentDirectory, SOURCE_DIR, MinecraftDataFolder);
            }
        }

        public MineCraftDataProvider(string minecraftDataFolder) {
            this.MinecraftDataFolder = minecraftDataFolder;

            if(!System.IO.Directory.Exists(DataPath)) {
                throw new Exception($"Directory Not Found {DataPath}");
            }
        }

        // Utility that removes image directories from the wiki dataset as they aren't needed
        private void CleanFiles() {
            var dirNames = new Stack<string>(System.IO.Directory.GetDirectories(DataPath, "images", SearchOption.AllDirectories));
            foreach (var dirName in dirNames) {
                System.IO.Directory.Delete(dirName, true);
                Util.WriteToConsole($"Deleting: {dirName}", ConsoleColor.DarkGreen);

            }
            Util.WriteToConsole($"Deleted {dirNames.Count} directories", ConsoleColor.Green );
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
                    Util.WriteToConsole($"{_DataFiles.Count()} Documents Found", ConsoleColor.Green);
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

            // Add html tag if number of occurences matches
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
                Util.WriteToConsole($"Augmenting Data: {wikiRecord.Title}", ConsoleColor.Gray);
                var web = new HtmlWeb();
                var doc = web.Load(wikiRecord.metadata.url);
                
                // Add tag infomation to wiki record
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
                Util.WriteToConsole($"Augmentation error {e.ToString()}");
                throw e;
            }
        }

        // Get next document section.  If all sections have been handled, move on to next file
        private string NextDocumentSection() {
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
                Util.WriteToConsole($"Adding: {CurWikiRecord.Title, -30} with {SubSections.Count, -10} items ({DataFiles.Count()} remaining)", ConsoleColor.DarkGreen);
                return NextDocumentSection();
            } 
            var nextDocument = SubSections.Dequeue();    
            return nextDocument;
        }

        // Add text to stack but only if meets length requirements
        private void AddText(Text curText, Queue<string> outputQueue) {
            var curTextNumWords = Util.NumWordsIn(curText.text);
            if (curTextNumWords >= MIN_REQUIRED_WORDS) {
                outputQueue.Enqueue(curText.text);
            }
        }

        // If text is numbered item (i.e. "4. carrots"), return "4" or 0 if not
        private int GetNumberedIndex(string text) {
            var parts = text.Split(".");
            if (parts.Count() < 2) {
                return 0;
            }
            if (int.TryParse(parts[0], out int n)) {
                return n;
            }
            return 0;
        }

        // Based on position is the next text item indented from the previous
        private bool IsSubItem(Text curText, Text nextText) {
            var inset = nextText.LeftMargin - curText.LeftMargin;
            if (inset < 40 && inset > 15) {
                return true;
            }

            var curTextIndex = GetNumberedIndex(curText.text);
            var nextTextIndex = GetNumberedIndex(nextText.text);
            if (curTextIndex == 0 || nextTextIndex == 0) {
                return false;
            }
            if (nextTextIndex == curTextIndex + 1) {
                return true;
            }
            return false;
        }

        // Do the two text items have the same indentation
        private bool IsPeer(Text curText, Text nextText) {
            var inset = nextText.LeftMargin - curText.LeftMargin;
            if (inset == 0) {
                return true;
            }
            return false;
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

        private void ProcessText(Text curText, Queue<Text> inputQueue, Queue<string> outputQueue, string section) {

            // Get next item, if none add final result
            Text nextText;
            if (!inputQueue.TryDequeue(out nextText)) {
                if (section == "") {
                    AddText(curText, outputQueue);
                }
                else {
                    outputQueue.Enqueue(section);
                }
                return;
            }

            // If next item a sub-item of the current ont
            if (IsSubItem(curText, nextText)) {
                if (section != "") {
                    section += $"{nextText.text}\n";
                }
                else {
                    section += $"{curText.text}\n{nextText.text}\n";
                }
                // Process the next item
                ProcessText(nextText, inputQueue, outputQueue, section);   

            }
            // Is next item a peer
            else if (IsPeer(curText, nextText)) {
                // If under the same section, add to section and keep processing
                if (section != "") {
                    section += $"{nextText.text}\n";

                    // Process the next item
                    ProcessText(nextText, inputQueue, outputQueue, section);    
                }
                // Otherwise add section and process next item
                else {
                    // Add accumulated section
                    AddText(curText, outputQueue);

                    // Process the next item
                    ProcessText(nextText, inputQueue, outputQueue, "");
                }
            }
            // Next item is not connect to this one
            else {
                if (section != "") {
                    outputQueue.Enqueue(section);
                }
                // Ignore wiki context boxes
                else if (curText.text == "Contents") {
                    if (!inputQueue.TryDequeue(out nextText)) {
                        return;
                    }
                    ProcessText(nextText, inputQueue, outputQueue, "");
                }
                else {
                    // Add the current item
                    AddText(curText, outputQueue);
                }

                // Process the next item
                ProcessText(nextText, inputQueue, outputQueue, "");
            }

        }
        // Filter on document section
        private Queue<string> GenerateDocumentSections(WikiRecord wikiRecord) {
            var outputQueue = new Queue<string>();

            // Add text sections
            var inputQueue = new Queue<Text>(wikiRecord.texts);
            if (inputQueue.TryDequeue(out Text nextText)) {
                ProcessText(nextText, inputQueue, outputQueue, ""); 
            }

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