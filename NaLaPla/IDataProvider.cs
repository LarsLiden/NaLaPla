namespace NaLaPla
{
    using Lucene.Net.Documents;

    public interface IDataProvider
    {
        public Document GetNextDocument();
    }
}