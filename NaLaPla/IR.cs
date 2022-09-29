namespace NaLaPla
{
    using Lucene.Net.Analysis.Standard;
    using Lucene.Net.QueryParsers.Classic;
    using Lucene.Net.Documents;
    using Lucene.Net.Index;
    using Lucene.Net.Search;
    using Lucene.Net.Store;
    using Lucene.Net.Util;

    public class IR {

        const string INDEX_FOLDER = "Index";

        const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        static bool done = false;

        static string IndexDirectory(string indexName = INDEX_FOLDER) {
            if(!System.IO.Directory.Exists(INDEX_FOLDER)) {
                    System.IO.Directory.CreateDirectory(INDEX_FOLDER);
            }
            return Path.Combine(Environment.CurrentDirectory, INDEX_FOLDER);
        }
        static public void CreateIndex(IDataProvider dataProvider) {

            Util.WriteToConsole("Generating search index...", ConsoleColor.Green);

            using var dir = FSDirectory.Open(IndexDirectory());
            var analyzer = new StandardAnalyzer(AppLuceneVersion);
            var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);
            indexConfig.OpenMode = OpenMode.CREATE; 
            var writer = new IndexWriter(dir, indexConfig);

            try
            {
                var doc = dataProvider.GetNextDocument();
                while (doc != null) 
                {
                    writer.AddDocument(doc);
                    doc = dataProvider.GetNextDocument();
                }
                writer.Flush(triggerMerge: false, applyAllDeletes: false);
            }
            catch
            {
                Lucene.Net.Index.IndexWriter.Unlock(dir);
                throw;
            }
            finally
            {
                writer.Dispose();
                analyzer.Dispose();
            }
        }

        public static List<string> GetRelatedDocuments(string text, int maxResults = 5) 
        {
            try {
                using var dir = FSDirectory.Open(IndexDirectory());
                using var analyzer = new StandardAnalyzer(AppLuceneVersion);
                var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);
                using var writer = new IndexWriter(dir, indexConfig);

                using var reader = writer.GetReader(applyAllDeletes: true);
                var searcher = new IndexSearcher(reader);

                QueryParser parser = new QueryParser(AppLuceneVersion, "body", analyzer);
                Query query = parser.Parse(text);

                var hits = searcher.Search(query, maxResults).ScoreDocs;

                // Display the output in a table
                Util.WriteToConsole(text, ConsoleColor.Red);
                Util.WriteToConsole($"{"Score",10}" + $" {"Topic",-15}", ConsoleColor.DarkRed);
                foreach (var hit in hits)
                {
                    var foundDoc = searcher.Doc(hit.Doc);
                    Console.WriteLine($"{hit.Score:f8}" + $" {foundDoc.Get("topic"),-15}");
                }

                var topDocuments = hits.Select(hit => searcher.Doc(hit.Doc).Get("body")).ToList();
                return topDocuments;
            }
        }
    }
}