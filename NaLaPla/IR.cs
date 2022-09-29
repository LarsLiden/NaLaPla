namespace NaLaPla
{
    using Lucene.Net.Analysis.Standard;
    using Lucene.Net.Documents;
    using Lucene.Net.Index;
    using Lucene.Net.Search;
    using Lucene.Net.Store;
    using Lucene.Net.Util;

    public class IR {

      //  static private IndexWriter _Writer = null;
        const string INDEX_FOLDER = "Index";

        const LuceneVersion AppLuceneVersion = LuceneVersion.LUCENE_48;

        static Lucene.Net.Analysis.Standard.StandardAnalyzer analyzer = null;
        static Lucene.Net.Index.IndexWriter writer = null;

        static bool done = false;

        static string IndexDirectory(string indexName = INDEX_FOLDER) {
            if(!System.IO.Directory.Exists(INDEX_FOLDER)) {
                    System.IO.Directory.CreateDirectory(INDEX_FOLDER);
            }
            return Path.Combine(Environment.CurrentDirectory, INDEX_FOLDER);
        }
        static public void CreateIndex() {

            using var dir = FSDirectory.Open(IndexDirectory());
            var analyzer = new StandardAnalyzer(AppLuceneVersion);
            var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);
            indexConfig.OpenMode = OpenMode.CREATE; 
            writer = new IndexWriter(dir, indexConfig);

            try
            {
                var doc = GetNextDocument();
                while (doc != null) 
                {
                    writer.AddDocument(doc);
                    doc = GetNextDocument();
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

        static Document GetNextDocument() {
            if (done) {
                return null;
            }
            var source = new
            {
                Name = "Kermit the Frog",
                FavoritePhrase = "The quick brown fox jumps over the lazy dog"
            };
            var doc = new Document
            {
                // StringField indexes but doesn't tokenize
                new StringField("name", 
                    source.Name, 
                    Field.Store.YES),
                new TextField("favoritePhrase", 
                    source.FavoritePhrase, 
                    Field.Store.YES)
            };
            done = true;
            return doc;
        }

        public static void TestQuery() {

            // Search with a phrase
            var phrase = new MultiPhraseQuery
            {
                new Term("favoritePhrase", "brown"),
                new Term("favoritePhrase", "fox")
            };
            Query(phrase);
        }

        static void Query(Query query) {
            using var dir = FSDirectory.Open(IndexDirectory());
            var analyzer = new StandardAnalyzer(AppLuceneVersion);
            var indexConfig = new IndexWriterConfig(AppLuceneVersion, analyzer);
            writer = new IndexWriter(dir, indexConfig);

            using var reader = writer.GetReader(applyAllDeletes: true);
            var searcher = new IndexSearcher(reader);
            var hits = searcher.Search(query, 20 /* top 20 */).ScoreDocs;

            // Display the output in a table
            Console.WriteLine($"{"Score",10}" +
                $" {"Name",-15}" +
                $" {"Favorite Phrase",-40}");
            foreach (var hit in hits)
            {
                var foundDoc = searcher.Doc(hit.Doc);
                Console.WriteLine($"{hit.Score:f8}" +
                    $" {foundDoc.Get("name"),-15}" +
                    $" {foundDoc.Get("favoritePhrase"),-40}");
            }
        }
    }
}