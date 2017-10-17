namespace MultiTermProcessor
{
    class Program
    {
        private static string SearchServiceName = "[Enter Search Service Name without .search.windows.net";
        private static string SearchServiceAPIKey = "[Enter Admin API Key]";
        private static string IndexName = "[Enter Azure Search Index Name]";
        private static string TextFolder = @"[Location of Text files]";
        private static string WorkingDir = @"[Working dir for processing of content]";

        static void Main(string[] args)
        {
            Process();
        }

        static void Process()
        { 
            // Initialize the content processor
            // Optionally can pass language (en, pt), min bm25 and sentences to summarize
            CSI.ContentProcessor cp = new CSI.ContentProcessor(SearchServiceName, SearchServiceAPIKey, IndexName, TextFolder, WorkingDir);

            // Clean the working dir
            cp.CleanWorkingDir();

            // Load all text into index to get values such as doc freq and avg word counts which are required for BM25 calculation
            cp.TextToLucene();

            // Calculate the unigram terms
            cp.CalculateBM25Terms();

            // Calculate the bigram terms
            cp.CalculateBM25TwoTerms();

            // Upload the content to Azure Search
            cp.UploadToAzureSearch();

            // Reduce SQLITE to unique bm25 terms
            //cp.ReduceSQLiteToBaseTerms();

            // Output the terms to a file output.txt in the working dir
            cp.OutputBM25ToFile();
        }
    }
}