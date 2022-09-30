using System.ComponentModel;

namespace NaLaPla
{
    using Lucene.Net.Documents;
    using Newtonsoft.Json.Linq;

    class MineCraftDataProvider : IDataProvider
    {
    
        private string MinecraftDataFolder = "";

        // Number of words to require before adding section to index
        const int MIN_REQUIRED_WORDS = 5;

        const string SOURCE_DIR = "GroundingData";

        // Stack of files to process
        private Stack<string> _DataFiles = null;

        // Stock of document section w/in file to process
        private Stack<string> SubSections = new Stack<string>();

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

        private Stack<string> DataFiles {
            get {
                if (_DataFiles == null) {
                    _DataFiles = new Stack<string>(System.IO.Directory.GetFiles(DataPath, "data.json", SearchOption.AllDirectories));
                    Util.WriteToConsole($"{_DataFiles.Count()} Documents Found", ConsoleColor.Green);
                }
                return _DataFiles;
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

                if (CurWikiRecord == null || CurWikiRecord.texts == null) {
                    return NextDocumentSection();    
                }
                var subTexts = CurWikiRecord.texts.Select(t => t.text);
                SubSections = GenerateDocumentSections(CurWikiRecord.texts);
                Util.WriteToConsole($"Adding: {CurWikiRecord.Title, -30} with {SubSections.Count, -10} items ({DataFiles.Count()} remaining)", ConsoleColor.DarkGreen);
                return NextDocumentSection();
            } 
            var nextDocument = SubSections.Pop();    
            return nextDocument;
        }

        // Add text to stack but only if meets length requirements
        private void AddText(Text curText, Stack<string> outputStack) {
            var curTextNumWords = Util.NumWordsIn(curText.text);
            if (curTextNumWords >= MIN_REQUIRED_WORDS) {
                outputStack.Push(curText.text);
            }
        }

        // Based on position is the next text item indented from the previous
        private bool IsSubItem(Text curText, Text nextText) {
            var inset = nextText.LeftMargin - curText.LeftMargin;
            if (inset < 40 && inset > 15) {
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

        private void ProcessText(Text curText, Queue<Text> inputQueue, Stack<string> outputStack, string section) {

            // Get next item, if none add final result
            Text nextText;
            if (!inputQueue.TryDequeue(out nextText)) {
                if (section == "") {
                    AddText(curText, outputStack);
                }
                else {
                    outputStack.Push(section);
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
                ProcessText(nextText, inputQueue, outputStack, section);   

            }
            // Is next item a peer
            else if (IsPeer(curText, nextText)) {
                // If under the same section, add to section and keep processing
                if (section != "") {
                    section += $"{nextText.text}\n";

                    // Process the next item
                    ProcessText(nextText, inputQueue, outputStack, section);    
                }
                // Otherwise add section and process next item
                else {
                    // Add accumulated section
                    AddText(curText, outputStack);

                    // Process the next item
                    ProcessText(nextText, inputQueue, outputStack, "");
                }
            }
            // Next item is not connect to this one
            else {
                if (section != "") {
                    outputStack.Push(section);
                }
                else {
                    // Add the current item
                    AddText(curText, outputStack);
                }

                // Process the next item
                ProcessText(nextText, inputQueue, outputStack, "");
            }

        }
        // Filter on document section
        private Stack<string> GenerateDocumentSections(List<Text> sections) {
            var outputStack = new Stack<string>();
            var inputQueue = new Queue<Text>(sections);

            Text nextText;
            if (inputQueue.TryDequeue(out nextText)) {
                ProcessText(nextText, inputQueue, outputStack, ""); 
            }
            return outputStack;
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