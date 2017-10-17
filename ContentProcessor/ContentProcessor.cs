using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Shingle;
using Lucene.Net.Analysis.Standard;
using Lucene.Net.Analysis.Tokenattributes;
using Lucene.Net.Documents;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace CSI
{
    public class ContentProcessor
    {

        private static double _MinBM25;
        private static int _SentencesToSummarize;
        private static string _language;

        private static string _SearchServiceName;
        private static string _SearchServiceAPIKey;
        private static string _IndexName;
        private static string _TextFolder;
        private static string _IndexFolder;
        private static string _IndexTermFolder;
        private static string _BM25OutputFile;
        private static string _SQLiteFolder;
        private static string _SQLiteTermsIndex = @"terms.sqlite";

        private static DateTime startTime = DateTime.Now;

        private static Analyzer StandardAnalyzer;
        private static Lucene.Net.Store.Directory indexDir;
        private static Lucene.Net.Store.Directory termDir;
        private static ISet<string> stopWords;

        private static Uri _serviceUri;
        private static HttpClient _httpClient;
        static string KeyField = "id";
        static string TermsField = "terms";
        static string SummaryField = "summary";
        static string ContentField = "content";
        static string FileNameField = "filename";
        static string FileTypeField = "filetype";
               

        public ContentProcessor(string SearchServiceName, string SearchServiceAPIKey, string IndexName, string TextFolder,
            string WorkingDir, string language = "en", double MinBM25 = 5.0, int SentencesToSummarize = 3)
        {
            _SearchServiceName = SearchServiceName;
            _SearchServiceAPIKey = SearchServiceAPIKey;
            _IndexName = IndexName;
            _TextFolder = TextFolder;

            _language = language;

            _MinBM25 = MinBM25;
            _SentencesToSummarize = SentencesToSummarize;

            _IndexFolder = Path.Combine(WorkingDir, "index");
            _IndexTermFolder = Path.Combine(WorkingDir, "indexTerms");
            _BM25OutputFile = Path.Combine(WorkingDir, "output.txt");
            _SQLiteFolder = Path.Combine(WorkingDir, "sqlite");

            // Set the language
            if (_language == "pt")
                stopWords = PortugueseStopWords.GetPortugueseStopWords();
            else  // default to english
                stopWords = EnglishStopWords.GetEnglishStopWords();

            // Configure Lucene
            StandardAnalyzer = new StandardAnalyzer(Lucene.Net.Util.Version.LUCENE_30);
            indexDir = Lucene.Net.Store.FSDirectory.Open(_IndexFolder);
            termDir = Lucene.Net.Store.FSDirectory.Open(_IndexTermFolder);


        }

        public void CleanWorkingDir()
        { 
            // Remove previous data if needed
            if (System.IO.Directory.Exists(_IndexFolder))
                System.IO.Directory.Delete(_IndexFolder, true);
            System.IO.Directory.CreateDirectory(_IndexFolder);
            if (File.Exists(_BM25OutputFile))
                File.Delete(_BM25OutputFile);

            // Configure SQLite
            if (System.IO.Directory.Exists(_SQLiteFolder))
                System.IO.Directory.Delete(_SQLiteFolder, true);
            System.IO.Directory.CreateDirectory(_SQLiteFolder);
            SQLiteConnection.CreateFile(Path.Combine(_SQLiteFolder, _SQLiteTermsIndex));

        }

        public static void CleanSingleTerms()
        {
            // If the single terms exists in the dual term, remove it
            Console.WriteLine(String.Format("[{0}] - If the single terms exists in the dual term, remove it...", DateTime.Now.Subtract(startTime)));
            using (var conn = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, _SQLiteTermsIndex) + ";Version=3;"))
            {
                // Get all the docid's...
                Console.WriteLine(String.Format("[{0}] - Get all docid's...", DateTime.Now.Subtract(startTime)));
                var docIdList = new List<int>();
                conn.Open();
                string sql = "select distinct docId from bm25";
                SQLiteCommand cmdSelect = new SQLiteCommand(sql, conn);
                var rdr = cmdSelect.ExecuteReader();
                while (rdr.Read())
                    docIdList.Add(Convert.ToInt32(rdr["docId"]));
                rdr.Close();

                int counter = 0;
                foreach (var docId in docIdList)
                {
                    counter++;
                    if (counter % 1000 == 0)
                        Console.WriteLine(String.Format("[{0}] - Cleaned {1} docid's...", DateTime.Now.Subtract(startTime), counter));

                    // Clean the docid of poor terms
                    sql = "select term from bm25 where docID = @docId and instr(term, ' ') > 0";
                    cmdSelect = new SQLiteCommand(sql, conn);
                    cmdSelect.Parameters.AddWithValue("docId", docId);
                    var rdrTerms = cmdSelect.ExecuteReader();
                    var terms = new List<string>();
                    while (rdrTerms.Read())
                    {
                        foreach (var term in rdrTerms[0].ToString().Split(new char[0], StringSplitOptions.RemoveEmptyEntries))
                        {
                            terms.Add(term);
                        }
                    }
                    var distinctTerms = terms.Distinct();
                    var transaction = conn.BeginTransaction();
                    sql = "delete from bm25 where docID = @docId and term = @singleTerm";
                    var cmdDelete = new SQLiteCommand(sql, conn);
                    cmdDelete.Prepare();
                    foreach (var term in distinctTerms)
                    {
                        cmdDelete.Parameters.AddWithValue("docId", docId);
                        cmdDelete.Parameters.AddWithValue("singleTerm", term);
                        cmdDelete.ExecuteNonQuery();
                    }

                    transaction.Commit();
                }
            }
        }

        public static bool isValidTerm(String str)
        {
            // Only allow valid terms 
            bool valid = false;

            // Ensure it is at least 3 chars
            if (str.Length <= 2)
                return false;

            // Ensure there is at least three alpha character
            int charCounter = 0;
            for (int i = 0; i < str.Length; i++)
            {
                if (Char.IsLetter(str[i]) == true)
                {
                    charCounter++;
                    if (charCounter == 3)
                    {
                        valid = true;
                        break;
                    }
                }
            }

            return valid;
        }

        public void ReduceSQLiteToBaseTerms()
        {
            using (var conn = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, _SQLiteTermsIndex) + ";Version=3;"))
            {
                conn.Open();
                SQLiteCommand stmt;
                stmt = new SQLiteCommand("create table bm25grouped (term nvarchar(256), bm25 double);", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("insert into bm25grouped select term, avg(bm25) from bm25 group by term;", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("drop table bm25;", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("VACUUM;", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("ALTER TABLE bm25grouped RENAME TO bm25;", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("create index idx_bm25_terms on bm25(term);", conn);
                stmt.ExecuteNonQuery();
            }
        }

        public void CalculateBM25Terms()
        {
            // This will find all viable uni-grams
            Lucene.Net.Index.IndexReader reader = DirectoryReader.Open(indexDir, true);
            Lucene.Net.Search.IndexSearcher searcher = new Lucene.Net.Search.IndexSearcher(reader);

            if (System.IO.Directory.Exists(_IndexTermFolder))
                System.IO.Directory.Delete(_IndexTermFolder, true);
            System.IO.Directory.CreateDirectory(_IndexTermFolder);

            using (var conn = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, _SQLiteTermsIndex) + ";Version=3;"))
            {
                conn.Open();
                SQLiteCommand stmt;
                stmt = new SQLiteCommand("PRAGMA synchronous=OFF", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("PRAGMA count_changes=OFF", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("PRAGMA journal_mode=MEMORY", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("PRAGMA temp_store=MEMORY", conn);
                stmt.ExecuteNonQuery();

                string sql = "DROP TABLE IF EXISTS termsPerDoc";
                SQLiteCommand cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create table termsPerDoc (docId int, term nvarchar(256), docCount int)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create index idx_termsPerDoc on termsPerDoc(term)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();

                sql = "DROP TABLE IF EXISTS termFreqInIndex";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create table termFreqInIndex (term nvarchar(256), freq int)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create index idx_termFreqInIndex on termFreqInIndex(term)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();

                sql = "DROP TABLE IF EXISTS wordCountsPerDoc";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create table wordCountsPerDoc (docId int, wordCount int)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create index idx_wordCountsPerDoc on wordCountsPerDoc(docId)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();

                Console.WriteLine(String.Format("[{0}] - Creating BM25 table...", DateTime.Now.Subtract(startTime)));
                sql = "create table bm25 (docId int, term nvarchar(256), bm25 double)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create index idx_bm25 on bm25(docId)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();


                // Get all docID's with some terms in them
                var docIdList = new List<int>();
                Query query = new MatchAllDocsQuery();
                var results = searcher.Search(query, null, Int32.MaxValue);
                List<int> DocIDs = new List<int>();

                Console.WriteLine(String.Format("[{0}] - Gathering all doc id's...", DateTime.Now.Subtract(startTime)));
                int counter = 0;
                foreach (var hit in results.ScoreDocs)
                {
                    counter++;
                    if (counter % 10000 == 0)
                        Console.WriteLine(String.Format("[{0}] - Retrieved {1} docid's...", DateTime.Now.Subtract(startTime), counter));
                    docIdList.Add(hit.Doc);
                }

                Console.WriteLine(String.Format("[{0}] - Find viable single terms...", DateTime.Now.Subtract(startTime)));
                var transaction = conn.BeginTransaction();
                sql = "insert into termsPerDoc (docId, term, docCount) values " +
                    "(@docId, @term, @docCount)";
                var cmdInsert = new SQLiteCommand(sql, conn);
                cmdInsert.Prepare();

                sql = "insert into wordCountsPerDoc (docId, wordCount) values " +
                    "(@docId, @wordCount)";
                var cmdInsertWordCount = new SQLiteCommand(sql, conn);
                cmdInsertWordCount.Prepare();

                int insCounter = 0;
                int transCounter = 0;
                foreach (var docId in docIdList)
                {
                    insCounter++;
                    if ((insCounter % 10000 == 0) || (transCounter > 1000000))
                    {
                        Console.WriteLine(String.Format("[{0}] - Wrote terms for {1} docs, {2} transactions in this batch..", DateTime.Now.Subtract(startTime), insCounter, transCounter));
                        transaction.Commit();
                        transaction = conn.BeginTransaction();
                        sql = "insert into termsPerDoc (docId, term, docCount) values " +
                            "(@docId, @term, @docCount)";
                        cmdInsert = new SQLiteCommand(sql, conn);
                        cmdInsert.Prepare();

                        sql = "insert into wordCountsPerDoc (docId, wordCount) values " +
                            "(@docId, @wordCount)";
                        cmdInsertWordCount = new SQLiteCommand(sql, conn);
                        cmdInsertWordCount.Prepare();

                        transCounter = 0;


                    }

                    Document d = searcher.Doc(docId);
                    var content = d.GetField("content").StringValue;

                    // Determine how frequent the terms are in the corpus
                    var termsInDoc = GetSingleTerms(docId, content);

                    foreach (var t in termsInDoc.termDetails)
                    {
                        cmdInsert.Parameters.AddWithValue("docId", docId);
                        cmdInsert.Parameters.AddWithValue("term", t.term);
                        cmdInsert.Parameters.AddWithValue("docCount", t.termCount);
                        cmdInsert.ExecuteNonQuery();
                        transCounter++;
                    }


                    // Apply the word count to SQLite
                    cmdInsertWordCount.Parameters.AddWithValue("docId", docId);
                    cmdInsertWordCount.Parameters.AddWithValue("wordCount", termsInDoc.wordCount);
                    cmdInsertWordCount.ExecuteNonQuery();
                    transCounter++;

                }
                transaction.Commit();

                Console.WriteLine(String.Format("[{0}] - {1}", DateTime.Now.Subtract(startTime), "Calculating the avg word count across all docs..."));
                sql = "select avg(wordCount) from wordCountsPerDoc";
                var cmdAWC = new SQLiteCommand(sql, conn);
                var rdrAWC = cmdAWC.ExecuteReader();
                double avgWordCount = 0;
                while (rdrAWC.Read())
                    avgWordCount = Convert.ToDouble(rdrAWC[0]);

                Console.WriteLine(String.Format("[{0}] - Getting total doc count...", DateTime.Now.Subtract(startTime)));
                double totalDocCount = 0;
                string sqlDocCount = "select count(*) from wordCountsPerDoc";
                var cmdDocCount = new SQLiteCommand(sqlDocCount, conn);
                var rdrDocCount = cmdDocCount.ExecuteReader();
                while (rdrDocCount.Read())
                {
                    totalDocCount = Convert.ToDouble(rdrDocCount[0]);
                }

                Console.WriteLine(String.Format("[{0}] - {1}", DateTime.Now.Subtract(startTime), "Calculating the term freq in the index for all terms..."));
                string sqlDocFreq = "insert into termFreqInIndex select term, count(*) from termsPerDoc group by term";
                var cmdDocFreq = new SQLiteCommand(sqlDocFreq, conn);
                cmdDocFreq.ExecuteNonQuery();

                Console.WriteLine(String.Format("[{0}] - {1}", DateTime.Now.Subtract(startTime), "Deleting term freq where it is too frequent or infrequent..."));
                sqlDocFreq = "delete from termFreqInIndex where freq = 1 or freq > @maxFreq";
                cmdDocFreq = new SQLiteCommand(sqlDocFreq, conn);
                cmdDocFreq.Parameters.AddWithValue("maxFreq", totalDocCount);
                cmdDocFreq.ExecuteNonQuery();

                // Now I have all the data I need to calculate bm25 for these terms
                // Iterate through items and start the calculation
                sql = "select ppd.docID, ppd.term, ppd.docCount, tfii.freq, wc.wordCount from termsPerDoc ppd, termFreqInIndex tfii, wordCountsPerDoc wc where ppd.term = tfii.term and wc.docId = ppd.docId";
                var cmdReadBM25 = new SQLiteCommand(sql, conn);
                var rdrReadBM25 = cmdReadBM25.ExecuteReader();
                double k = 1.2;
                double b = 0.75;

                Console.WriteLine(String.Format("[{0}] - Writing bm25 terms to SQLite...", DateTime.Now.Subtract(startTime)));
                transaction = conn.BeginTransaction();
                sql = "insert into bm25 (docId, term, bm25) values (@docId, @term, @bm25)";
                var cmdInsertBM25 = new SQLiteCommand(sql, conn);
                cmdInsertBM25.Prepare();
                int bm25Counter = 0;
                while (rdrReadBM25.Read())
                {
                    if (bm25Counter % 100000 == 0)
                    {
                        Console.WriteLine(String.Format("[{0}] - Wrote {1} bm25 terms...", DateTime.Now.Subtract(startTime), bm25Counter));
                        transaction.Commit();
                        transaction = conn.BeginTransaction();
                        sql = "insert into bm25 (docId, term, bm25) values (@docId, @term, @bm25)";
                        cmdInsertBM25 = new SQLiteCommand(sql, conn);
                        cmdInsertBM25.Prepare();
                    }

                    string docId = rdrReadBM25["docId"].ToString();
                    string term = rdrReadBM25["term"].ToString();
                    double wordCountOfThisDoc = Convert.ToDouble(rdrReadBM25["wordCount"]);
                    double termFreqInDocument = Convert.ToDouble(rdrReadBM25["docCount"]);
                    double termFreqInIndex = Convert.ToDouble(rdrReadBM25["freq"]);

                    double bm25 = Math.Log(Convert.ToDouble((totalDocCount - termFreqInIndex + 0.5) / (termFreqInIndex + 0.5))) * (termFreqInDocument * (k + 1)) / (termFreqInDocument + k * (1 - b + (b * wordCountOfThisDoc / avgWordCount)));

                    if (bm25 > _MinBM25)
                    {
                        bm25Counter++;
                        cmdInsertBM25.Parameters.AddWithValue("docId", docId);
                        cmdInsertBM25.Parameters.AddWithValue("term", term);
                        cmdInsertBM25.Parameters.AddWithValue("bm25", bm25);
                        cmdInsertBM25.ExecuteNonQuery();
                    }
                }
                transaction.Commit();

            }



        }

        public void CalculateBM25TwoTerms()
        {
            // Use the bm25 terms that were identified to determing potential two term phrases.  I will do this by taking the shingle 
            // before the existing stem and create a new 2 two phrase from it
            // Using these new phrases, go through the same process to see if it is a good BM25 term

            Lucene.Net.Index.IndexReader reader = DirectoryReader.Open(indexDir, true);
            Lucene.Net.Search.IndexSearcher searcher = new Lucene.Net.Search.IndexSearcher(reader);

            using (var conn = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, _SQLiteTermsIndex) + ";Version=3;"))
            {
                conn.Open();
                SQLiteCommand stmt;
                stmt = new SQLiteCommand("PRAGMA synchronous=OFF", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("PRAGMA count_changes=OFF", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("PRAGMA journal_mode=MEMORY", conn);
                stmt.ExecuteNonQuery();
                stmt = new SQLiteCommand("PRAGMA temp_store=MEMORY", conn);
                stmt.ExecuteNonQuery();

                string sql = "DROP TABLE IF EXISTS phrasesPerDoc";
                SQLiteCommand cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create table phrasesPerDoc (docId int, term nvarchar(256), docCount int)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create index idx_phrasesPerDoc on phrasesPerDoc(term)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();

                sql = "DROP TABLE IF EXISTS termFreqInIndex";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create table termFreqInIndex (term nvarchar(256), freq int)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create index idx_termFreqInIndex on termFreqInIndex(term)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();

                sql = "DROP TABLE IF EXISTS wordCountsPerDoc";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create table wordCountsPerDoc (docId int, wordCount int)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();
                sql = "create index idx_wordCountsPerDoc on wordCountsPerDoc(docId)";
                cmd = new SQLiteCommand(sql, conn);
                cmd.ExecuteNonQuery();

                // Get all docID's with some terms in them
                var docIdList = new List<int>();
                sql = "select distinct docId from bm25 order by docId";
                cmd = new SQLiteCommand(sql, conn);
                var rdr = cmd.ExecuteReader();
                var counter = 0;
                Console.WriteLine(String.Format("[{0}] - Getting all docId's with terms...", DateTime.Now.Subtract(startTime)));
                while (rdr.Read())
                {
                    counter++;
                    if (counter % 10000 == 0)
                        Console.WriteLine(String.Format("[{0}] - Retrieved {1} docid's...", DateTime.Now.Subtract(startTime), counter));
                    docIdList.Add(Convert.ToInt32(rdr["docId"]));
                }
                rdr.Close();

                Console.WriteLine(String.Format("[{0}] - Find viable 2 term phrases...", DateTime.Now.Subtract(startTime)));
                var transaction = conn.BeginTransaction();
                sql = "insert into phrasesPerDoc (docId, term, docCount) values " +
                    "(@docId, @term, @docCount)";
                var cmdInsert = new SQLiteCommand(sql, conn);
                cmdInsert.Prepare();

                sql = "insert into wordCountsPerDoc (docId, wordCount) values " +
                    "(@docId, @wordCount)";
                var cmdInsertWordCount = new SQLiteCommand(sql, conn);
                cmdInsertWordCount.Prepare();

                int insCounter = 0;
                foreach (var docId in docIdList)
                {
                    insCounter++;
                    if (insCounter % 10000 == 0)
                    {
                        Console.WriteLine(String.Format("[{0}] - Wrote phrases for {1} docs...", DateTime.Now.Subtract(startTime), insCounter));
                        transaction.Commit();
                        transaction = conn.BeginTransaction();
                        sql = "insert into phrasesPerDoc (docId, term, docCount) values " +
                            "(@docId, @term, @docCount)";
                        cmdInsert = new SQLiteCommand(sql, conn);
                        cmdInsert.Prepare();

                    }
                    var termList = new List<string>();
                    sql = "select term from bm25 where docId = @docId";
                    cmd = new SQLiteCommand(sql, conn);
                    cmd.Parameters.AddWithValue("docId", docId);
                    rdr = cmd.ExecuteReader();
                    while (rdr.Read())
                    {
                        termList.Add(rdr["term"].ToString());
                    }

                    Document d = searcher.Doc(docId);
                    var content = d.GetField("content").StringValue;
                    var termPosition = new List<string>();

                    using (var stringReader = new StringReader(content))
                    {
                        StandardTokenizer stdToken = new StandardTokenizer(Lucene.Net.Util.Version.LUCENE_30, stringReader);
                        //var ShingleStream = new ShingleMatrixFilter(tokenStream, 2, 2);
                        TokenStream ShingleStreamCombined = new ShingleMatrixFilter(new StopFilter(true, new ASCIIFoldingFilter(new LowerCaseFilter(stdToken)), stopWords), 2, 2);
                        ShingleStreamCombined.Reset();
                        var charTermAttribute = ShingleStreamCombined.GetAttribute<ITermAttribute>();

                        while (ShingleStreamCombined.IncrementToken())
                        {
                            // Ensure none of the terms are numeric or money
                            var allterms = charTermAttribute.Term.Split('_');
                            bool validTerms = true;
                            foreach (var term in allterms)
                            {
                                if (isValidTerm(term) == false)
                                    validTerms = false;
                            }

                            if (validTerms)
                                termPosition.Add(charTermAttribute.Term.Replace("_", " "));
                        }
                    }

                    var ViablePhrases = new List<string>();
                    foreach (var term in termList)
                    {
                        var result = termPosition.Where(item => item.Contains(term));
                        foreach (var p in result)
                        {
                            ViablePhrases.Add(p);
                        }
                    }

                    // Find how often this phrase is in the current doc
                    var PhraseCounts = ViablePhrases.GroupBy(x => x)
                            .Select(g => new { term = g.Key, count = g.Count() });

                    foreach (var phrase in PhraseCounts)
                    {
                        cmdInsert.Parameters.AddWithValue("docId", docId);
                        cmdInsert.Parameters.AddWithValue("term", phrase.term);
                        cmdInsert.Parameters.AddWithValue("docCount", phrase.count);
                        cmdInsert.ExecuteNonQuery();
                    }

                    // Determine how frequent the terms are in the corpus
                    var terms = GetSingleTerms(docId, content);

                    // Apply the word count to SQLite
                    cmdInsertWordCount.Parameters.AddWithValue("docId", docId);
                    cmdInsertWordCount.Parameters.AddWithValue("wordCount", terms.wordCount);
                    cmdInsertWordCount.ExecuteNonQuery();
                }
                transaction.Commit();

                Console.WriteLine(String.Format("[{0}] - {1}", DateTime.Now.Subtract(startTime), "Calculating the avg word count across all docs..."));
                sql = "select avg(wordCount) from wordCountsPerDoc";
                var cmdAWC = new SQLiteCommand(sql, conn);
                var rdrAWC = cmdAWC.ExecuteReader();
                double avgWordCount = 0;
                while (rdrAWC.Read())
                    avgWordCount = Convert.ToDouble(rdrAWC[0]);

                Console.WriteLine(String.Format("[{0}] - Getting total doc count...", DateTime.Now.Subtract(startTime)));
                double totalDocCount = 0;
                string sqlDocCount = "select count(*) from wordCountsPerDoc";
                var cmdDocCount = new SQLiteCommand(sqlDocCount, conn);
                var rdrDocCount = cmdDocCount.ExecuteReader();
                while (rdrDocCount.Read())
                {
                    totalDocCount = Convert.ToDouble(rdrDocCount[0]);
                }

                Console.WriteLine(String.Format("[{0}] - {1}", DateTime.Now.Subtract(startTime), "Calculating the term freq in the index for all terms..."));
                string sqlDocFreq = "insert into termFreqInIndex select term, count(*) from phrasesPerDoc group by term";
                var cmdDocFreq = new SQLiteCommand(sqlDocFreq, conn);
                cmdDocFreq.ExecuteNonQuery();

                Console.WriteLine(String.Format("[{0}] - {1}", DateTime.Now.Subtract(startTime), "Deleting term freq where it is too frequent or infrequent..."));
                sqlDocFreq = "delete from termFreqInIndex where freq = 1 or freq > @maxFreq";
                cmdDocFreq = new SQLiteCommand(sqlDocFreq, conn);
                cmdDocFreq.Parameters.AddWithValue("maxFreq", totalDocCount);
                cmdDocFreq.ExecuteNonQuery();

                // Now I have all the data I need to calculate bm25 for these terms
                // Iterate through items and start the calculation
                sql = "select ppd.docID, ppd.term, ppd.docCount, tfii.freq, wc.wordCount from phrasesPerDoc ppd, termFreqInIndex tfii, wordCountsPerDoc wc where ppd.term = tfii.term and wc.docId = ppd.docId";
                var cmdReadBM25 = new SQLiteCommand(sql, conn);
                var rdrReadBM25 = cmdReadBM25.ExecuteReader();
                double k = 1.2;
                double b = 0.75;

                Console.WriteLine(String.Format("[{0}] - Writing bm25 terms to SQLite...", DateTime.Now.Subtract(startTime)));
                transaction = conn.BeginTransaction();
                sql = "insert into bm25 (docId, term, bm25) values (@docId, @term, @bm25)";
                var cmdInsertBM25 = new SQLiteCommand(sql, conn);
                cmdInsertBM25.Prepare();
                int bm25Counter = 0;
                while (rdrReadBM25.Read())
                {
                    if (bm25Counter % 100000 == 0)
                    {
                        Console.WriteLine(String.Format("[{0}] - Wrote {1} bm25 terms...", DateTime.Now.Subtract(startTime), bm25Counter));
                        transaction.Commit();
                        transaction = conn.BeginTransaction();
                        sql = "insert into bm25 (docId, term, bm25) values (@docId, @term, @bm25)";
                        cmdInsertBM25 = new SQLiteCommand(sql, conn);
                        cmdInsertBM25.Prepare();
                    }

                    string docId = rdrReadBM25["docId"].ToString();
                    string term = rdrReadBM25["term"].ToString();
                    double wordCountOfThisDoc = Convert.ToDouble(rdrReadBM25["wordCount"]);
                    double termFreqInDocument = Convert.ToDouble(rdrReadBM25["docCount"]);
                    double termFreqInIndex = Convert.ToDouble(rdrReadBM25["freq"]);

                    double bm25 = Math.Log(Convert.ToDouble((totalDocCount - termFreqInIndex + 0.5) / (termFreqInIndex + 0.5))) * (termFreqInDocument * (k + 1)) / (termFreqInDocument + k * (1 - b + (b * wordCountOfThisDoc / avgWordCount)));

                    if (bm25 > _MinBM25)
                    {
                        bm25Counter++;
                        cmdInsertBM25.Parameters.AddWithValue("docId", docId);
                        cmdInsertBM25.Parameters.AddWithValue("term", term);
                        cmdInsertBM25.Parameters.AddWithValue("bm25", bm25);
                        cmdInsertBM25.ExecuteNonQuery();
                    }
                }
                transaction.Commit();

            }


        }
        public void TextToLucene()
        {
            // Load all text files into a Lucene index
            var files = System.IO.Directory.EnumerateFiles(_TextFolder, "*.txt", System.IO.SearchOption.AllDirectories);
            int counter = 0;
            IndexWriter writer;
            if (_language == "pt")
                writer = new IndexWriter(indexDir, new CustomStandardPortugueseAnalyzer(), IndexWriter.MaxFieldLength.UNLIMITED);
            else // default to english
                writer = new IndexWriter(indexDir, new CustomStandardEnglishAnalyzer(), IndexWriter.MaxFieldLength.UNLIMITED);

            Console.WriteLine(String.Format("[{0}] - {1}", DateTime.Now.Subtract(startTime), "Applying text to Lucene index..."));

            //foreach (var file in files)
            Parallel.ForEach(files, file =>
            {
                Interlocked.Increment(ref counter);
                if (counter % 1000 == 0) // Don't output too often as this will really impact perf
                    Console.WriteLine(String.Format("[{0}] - {1}", DateTime.Now.Subtract(startTime), counter));
                string content = System.IO.File.ReadAllText(file);
                writer.AddDocument(CreateDocument(file, content));
            });

            writer.Commit();
            writer.Dispose();

            Console.WriteLine(String.Format("Total Time Loading Content to Lucene: {0}", DateTime.Now.Subtract(startTime)));

        }

        private static Document CreateDocument(string file, string content)
        {
            Document d = new Document();
            Field f = new Field("file", "", Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.WITH_POSITIONS_OFFSETS);
            f.SetValue(file);
            d.Add(f);
            f = new Field("content", "", Field.Store.YES, Field.Index.ANALYZED, Field.TermVector.WITH_POSITIONS_OFFSETS);
            f.SetValue(content);
            d.Add(f);
            return d;
        }

        static TermVectorV2 GetSingleTerms(int docId, string content)
        {
            var termVector = new Dictionary<string, int>();
            var termVectorList = new TermVectorV2();
            termVectorList.termDetails = new List<TermDetails>();
            termVectorList.docId = docId;

            using (var stringReader = new StringReader(content))
            {

                StandardTokenizer stdToken = new StandardTokenizer(Lucene.Net.Util.Version.LUCENE_30, stringReader);
                TokenStream ShingleStreamCombined = new StopFilter(true, new ASCIIFoldingFilter(new LowerCaseFilter(stdToken)), stopWords);
                ShingleStreamCombined.Reset();
                var charTermAttribute = ShingleStreamCombined.GetAttribute<ITermAttribute>();

                int wordCount = 0;
                // To stem words 
                while (ShingleStreamCombined.IncrementToken())
                {
                    var term = charTermAttribute.Term;
                    wordCount++;
                    int value;
                    termVector.TryGetValue(term, out value);

                    // Add item if it is not a number or > 2 chars
                    if (isValidTerm(term))
                    {
                        termVector[term] = value + 1;
                    }

                }

                termVectorList.wordCount = wordCount;

                foreach (var item in termVector)
                {
                    var tv = new TermDetails();
                    tv.term = item.Key;
                    tv.termCount = item.Value;
                    termVectorList.termDetails.Add(tv);
                }
            }

            return termVectorList;
        }

        static Dictionary<int, int> FindDocIdsWithTerm(string term)
        {
            // using the specified term, find the documents where the term exits
            var docIdList = new Dictionary<int, int>();
            using (var connReader = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, _SQLiteTermsIndex) + ";Version=3;"))
            {
                connReader.Open();
                string sql = "select docId, docCount from termsPerDoc where term = @term";
                SQLiteCommand cmdSelect = new SQLiteCommand(sql, connReader);
                cmdSelect.Parameters.AddWithValue("term", term);
                var rdr = cmdSelect.ExecuteReader();
                while (rdr.Read())
                    docIdList[Convert.ToInt32(rdr["docId"])] = Convert.ToInt32(rdr["docCount"]);

            }
            return docIdList;
        }

        static Dictionary<int, int> FindDocIdsWithPhrase(string phrase)
        {
            // using the specified term, find the documents where the term exits
            var docIdList = new Dictionary<int, int>();
            using (var connReader = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, _SQLiteTermsIndex) + ";Version=3;"))
            {
                connReader.Open();
                string sql = "select docId, docCount from phrasesPerDoc where term = @term";
                SQLiteCommand cmdSelect = new SQLiteCommand(sql, connReader);
                cmdSelect.Parameters.AddWithValue("term", phrase);
                var rdr = cmdSelect.ExecuteReader();
                while (rdr.Read())
                    docIdList[Convert.ToInt32(rdr["docId"])] = Convert.ToInt32(rdr["docCount"]);

            }
            return docIdList;
        }

        public void OutputBM25ToFile()
        {
            // This is a test function to export data 
            using (var conn = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, _SQLiteTermsIndex) + ";Version=3;"))
            {
                conn.Open();
                string sql = "select docId, term, bm25 from bm25 order by docId, bm25 desc";
                var cmd = new SQLiteCommand(sql, conn);
                var rdr = cmd.ExecuteReader();
                var counter = 0;
                using (StreamWriter writer = new StreamWriter(_BM25OutputFile))
                {
                    Console.WriteLine(String.Format("[{0}] - Writing bm25 data to file...", DateTime.Now.Subtract(startTime)));
                    while (rdr.Read())
                    {
                        counter++;
                        if (counter % 10000 == 0)
                            Console.WriteLine(String.Format("[{0}] - Wrote {1} bm25 terms to file...", DateTime.Now.Subtract(startTime), counter));
                        writer.WriteLine(rdr["docId"].ToString() + "\t" + rdr["term"].ToString() + "\t" + rdr["bm25"].ToString());
                    }
                }
            }
        }

        public void UploadToAzureSearch()
        {
            // Coonfigure the azure search index
            _serviceUri = new Uri("https://" + _SearchServiceName + ".search.windows.net");
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("api-key", _SearchServiceAPIKey);

            CreateIndex(_IndexName);

            // At this point we have the key phrases and the core text content, take all of this and load it in to Azure Search
            Lucene.Net.Index.IndexReader reader = DirectoryReader.Open(indexDir, true);
            Lucene.Net.Search.IndexSearcher searcher = new
            Lucene.Net.Search.IndexSearcher(reader);

            List<int> DocIDs = new List<int>();
            Query query = new MatchAllDocsQuery();
            var results = searcher.Search(query, null, Int32.MaxValue);

            Console.WriteLine("Gathering all doc id's...");
            foreach (var hit in results.ScoreDocs)
            {
                Document d = searcher.Doc(hit.Doc);
                DocIDs.Add(hit.Doc);
            }

            int counter = 0;
            using (var conn = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, _SQLiteTermsIndex) + ";Version=3;"))
            {
                conn.Open();

                //foreach (var docID in DocIDs)
                Parallel.ForEach(DocIDs, new ParallelOptions { MaxDegreeOfParallelism = 8 }, docID =>
                {
                    Interlocked.Increment(ref counter);
                    if (counter % 1000 == 0)
                        Console.WriteLine(counter + ": " + docID);

                    // Get the text for this document
                    Document d = searcher.Doc(docID);
                    var fieldVal = d.GetField("content");
                    string content = ((Field)fieldVal).StringValue;
                    fieldVal = d.GetField("file");
                    string file = ((Field)fieldVal).StringValue;
                    //file = file.Substring(file.LastIndexOf("\\") + 1);
                    file = file.Replace("\\", "||");

                    content = content.Replace("\"", "'");
                    content = content.Replace("\\", "");

                    // Get all phrases for this docID sorted by bm25 desc
                    Query q2 = NumericRangeQuery.NewIntRange("docId", docID, docID, true, true);
                    string sql = "select docId, term, bm25 from bm25 where docId = @docId order by bm25 desc";
                    var cmd = new SQLiteCommand(sql, conn);
                    cmd.Parameters.AddWithValue("docId", docID);
                    var rdr = cmd.ExecuteReader();
                    List<Phrase> PhraseList = new List<Phrase>();
                    while (rdr.Read())
                    {
                        PhraseList.Add(new Phrase
                        {
                            originalTerm = rdr["term"].ToString(),
                            bm25 = Convert.ToDouble(rdr["bm25"])
                        });
                    }

                    var SortedPhrases = PhraseList.OrderByDescending(x => x.bm25).Select(y => y.originalTerm).ToList();
                    JArray jTerms = JArray.FromObject(SortedPhrases);

                    // Calculate the summary
                    String[] sentences = content.Split('!', '.', '?');
                    var MatchList = GetBestMatches(sentences, PhraseList).Take(_SentencesToSummarize).OrderBy(x => x.Sentence).ToList();
                    List<string> SentenceList = new List<string>();
                    string summary = string.Empty;
                    for (int i = 0; i < MatchList.Count; i++)
                    {
                        SentenceList.Add(sentences[MatchList[i].Sentence]);
                    }
                    // If there are no sentences found, just take the first three
                    if (SentenceList.Count == 0)
                    {
                        for (int i = 0; i < Math.Min(_SentencesToSummarize, sentences.Count()); i++)
                        {
                            SentenceList.Add(sentences[0]);
                        }
                    }

                    var DeDupedSentenceList = SentenceList.Distinct().Select(s => s.Trim()).ToList();
                    JArray jSummaries = JArray.FromObject(DeDupedSentenceList);

                    string fileType = file.Substring(file.LastIndexOf("||") + 1).ToLower();
                    fileType = fileType.Substring(fileType.IndexOf(".") + 1);
                    if (fileType.IndexOf(".") > -1)
                        fileType = fileType.Substring(0, fileType.IndexOf("."));

                    string json = "{\"value\": [";
                    json += "   { " +
                        "       \"@search.action\": \"upload\"," +
                        "       \"" + KeyField + "\": \"" + docID + "\"," +
                        "       \"" + FileNameField + "\": \"" + file + "\"," +
                        "       \"" + ContentField + "\": \"" + content.Substring(0, Math.Min(200000, content.Length)) + "\"," +
                        "       \"" + FileTypeField + "\": \"" + fileType + "\"," +
                        "       \"" + TermsField + "\": " + jTerms.ToString() + "," +
                        "       \"" + SummaryField + "\": " + jSummaries.ToString() +
                    "   } ";
                    json += "   ]} ";
                    UploadDocuments(_IndexName, json);
                });

            }

            Console.WriteLine(String.Format("[{0}] - Completed uploading content to Azure Search", DateTime.Now.Subtract(startTime)));
        }


        public void AppendToAzureSearch(string KeyField)
        {
            // Configure the azure search index by adding the terms and summary fields and then merging the content
            _serviceUri = new Uri("https://" + _SearchServiceName + ".search.windows.net");
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("api-key", _SearchServiceAPIKey);

            AppendFieldsToIndex(_IndexName);

            // At this point we have the key phrases and the core text content, take all of this and load it in to Azure Search
            Lucene.Net.Index.IndexReader reader = DirectoryReader.Open(indexDir, true);
            Lucene.Net.Search.IndexSearcher searcher = new
            Lucene.Net.Search.IndexSearcher(reader);

            List<int> DocIDs = new List<int>();
            Query query = new MatchAllDocsQuery();
            var results = searcher.Search(query, null, Int32.MaxValue);

            Console.WriteLine("Gathering all doc id's...");
            foreach (var hit in results.ScoreDocs)
            {
                Document d = searcher.Doc(hit.Doc);
                DocIDs.Add(hit.Doc);
            }

            int counter = 0;
            using (var conn = new SQLiteConnection("Data Source=" + Path.Combine(_SQLiteFolder, _SQLiteTermsIndex) + ";Version=3;"))
            {
                conn.Open();

                //foreach (var docID in DocIDs)
                Parallel.ForEach(DocIDs, new ParallelOptions { MaxDegreeOfParallelism = 8 }, docID =>
                {
                    Interlocked.Increment(ref counter);
                    Console.WriteLine(counter + ": " + docID);

                    // Get the text for this document
                    Document d = searcher.Doc(docID);
                    var fieldVal = d.GetField("content");
                    string content = ((Field)fieldVal).StringValue;
                    fieldVal = d.GetField("file");
                    string file = ((Field)fieldVal).StringValue;
                    file = file.Substring(file.LastIndexOf("\\") + 1);

                    content = content.Replace("\"", "'");
                    content = content.Replace("\\", "");

                    //.GetStringValue();

                    // Get all phrases for this docID sorted by bm25 desc
                    Query q2 = NumericRangeQuery.NewIntRange("docId", docID, docID, true, true);
                    string sql = "select docId, term, bm25 from bm25 where docId = @docId order by bm25 desc";
                    var cmd = new SQLiteCommand(sql, conn);
                    cmd.Parameters.AddWithValue("docId", docID);
                    var rdr = cmd.ExecuteReader();
                    List<Phrase> PhraseList = new List<Phrase>();
                    while (rdr.Read())
                    {
                        PhraseList.Add(new Phrase
                        {
                            originalTerm = rdr["term"].ToString(),
                            bm25 = Convert.ToDouble(rdr["bm25"])
                        });
                    }

                    var SortedPhrases = PhraseList.OrderByDescending(x => x.bm25).Select(y => y.originalTerm).ToList();
                    JArray jTerms = JArray.FromObject(SortedPhrases);

                    // Calculate the summary
                    String[] sentences = content.Split('!', '.', '?');
                    var MatchList = GetBestMatches(sentences, PhraseList).Take(_SentencesToSummarize).OrderBy(x => x.Sentence).ToList();
                    List<string> SentenceList = new List<string>();
                    string summary = string.Empty;
                    for (int i = 0; i < MatchList.Count; i++)
                    {
                        SentenceList.Add(sentences[MatchList[i].Sentence]);
                    }
                    // If there are no sentences found, just take the first three
                    if (SentenceList.Count == 0)
                    {
                        for (int i = 0; i < Math.Min(_SentencesToSummarize, sentences.Count()); i++)
                        {
                            SentenceList.Add(sentences[0]);
                        }
                    }

                    var DeDupedSentenceList = SentenceList.Distinct().Select(s => s.Trim()).ToList();
                    JArray jSummaries = JArray.FromObject(DeDupedSentenceList);

                    string json = "{\"value\": [";
                    json += "   { " +
                        "       \"@search.action\": \"merge\"," +
                        "       \"" + KeyField + "\": \"" + file.Replace(".txt","") + "\"," +     // In this case use the file as the id - .txt
                        "       \"" + TermsField + "\": " + jTerms.ToString() + "," +
                        "       \"" + SummaryField + "\": " + jSummaries.ToString() +
                    "   } ";
                    json += "   ]} ";
                    UploadDocuments(_IndexName, json);
                });

            }

            Console.WriteLine(String.Format("[{0}] - Completed uploading content to Azure Search", DateTime.Now.Subtract(startTime)));
        }

        public static bool UploadDocuments(string _IndexName, string json)
        {

            try
            {
                Uri uri = new Uri(_serviceUri, "/indexes/" + _IndexName + "/docs/index");

                HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Post, uri, json);
                AzureSearchHelper.EnsureSuccessfulSearchResponse(response);
                var option = AzureSearchHelper.DeserializeJson<dynamic>(response.Content.ReadAsStringAsync().Result);

            }
            catch (Exception e)
            {
                Console.WriteLine("Failed Uploading Data: {0}", e.Message.ToString());
                return false;
            }

            return true;
        }


        public static bool CreateIndex(string _IndexName)
        {
            try
            {
                Uri uri = new Uri(_serviceUri, "/indexes/" + _IndexName);

                HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Delete, uri);
                AzureSearchHelper.EnsureSuccessfulSearchResponse(response);
                var option = AzureSearchHelper.DeserializeJson<dynamic>(response.Content.ReadAsStringAsync().Result);
            }
            catch (Exception e)
            {
                if (e.Message.IndexOf("No index with the name") == -1)
                {
                    Console.WriteLine("Failed Index Deletion: {0}", e.Message.ToString());
                    return false;
                }
            }

            try
            {
                string json = File.ReadAllText("schema.json");
                json = json.Replace("INDEXNAME", _IndexName);
                Uri uri = new Uri(_serviceUri, "/indexes/" + _IndexName);

                HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Put, uri, json);
                AzureSearchHelper.EnsureSuccessfulSearchResponse(response);
                var option = AzureSearchHelper.DeserializeJson<dynamic>(response.Content.ReadAsStringAsync().Result);
            }
            catch (Exception e)
            {
                Console.WriteLine("Failed Index Creation: {0}", e.Message.ToString());
                return false;
            }

            return true;
        }

        private static bool AppendFieldsToIndex(string _IndexName)
        {
            try
            {
                Uri uri = new Uri(_serviceUri, "/indexes/" + _IndexName);

                HttpResponseMessage response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Get, uri);
                AzureSearchHelper.EnsureSuccessfulSearchResponse(response);
                var json = response.Content.ReadAsStringAsync().Result;
                
                string newFields = " { " +
                    "   'name': 'summary', " +
                    "   'type': 'Collection(Edm.String)', " +
                    "   'searchable': true, " +
                    "   'filterable': false, " +
                    "   'retrievable': true, " +
                    "   'sortable': false, " +
                    "   'facetable': false, " +
                    "   'key': false, " +
                    "   'indexAnalyzer': null, " +
                    "   'searchAnalyzer': null, " +
                    "   'analyzer': null, " +
                    "   'synonymMaps': [] " +
                    " }, " +
                    " { " +
                    "   'name': 'terms', " +
                    "   'type': 'Collection(Edm.String)', " +
                    "   'searchable': true, " +
                    "   'filterable': true, " +
                    "   'retrievable': true, " +
                    "   'sortable': false, " +
                    "   'facetable': true, " +
                    "   'key': false, " +
                    "   'indexAnalyzer': null, " +
                    "   'searchAnalyzer': null, " +
                    "   'analyzer': null, " +
                    "   'synonymMaps': [] " +
                    " }, ";

                int fieldLoc = json.IndexOf("[", json.IndexOf("\"fields\":"));

                string newIndex = json.Substring(0, fieldLoc+1) + newFields + json.Substring(fieldLoc+1);

                response = AzureSearchHelper.SendSearchRequest(_httpClient, HttpMethod.Put, uri, newIndex);
                AzureSearchHelper.EnsureSuccessfulSearchResponse(response);
                var option = AzureSearchHelper.DeserializeJson<dynamic>(response.Content.ReadAsStringAsync().Result);

            }
            catch (Exception e)
            {
                Console.WriteLine("Failed to update index schema: {0}", e.Message.ToString());
                return false;
            }


            return true;
        }

        static List<Match> GetBestMatches(string[] sentences, List<Phrase> words)
        {
            List<Match> matchList = new List<Match>();
            int counter = 0;
            // Take the 10 best words
            words = words.Take(10).ToList();
            foreach (var sentence in sentences)
            {
                double count = 0;

                Match match = new Match();
                foreach (Phrase phrase in words)
                {
                    if ((sentence.ToLower().IndexOf(phrase.originalTerm) > -1) &&
                        (sentence.Length > 20) && (WordCount(sentence) >= 3))
                        count += phrase.bm25;
                }

                if (count > 0)
                    matchList.Add(new Match { Sentence = counter, Total = count });
                counter++;
            }

            return matchList.OrderByDescending(x => x.Total).ToList();
        }

        static int WordCount(string text)
        {
            // Calculate total word count in text
            int wordCount = 0, index = 0;

            while (index < text.Length)
            {
                // check if current char is part of a word
                while (index < text.Length && !char.IsWhiteSpace(text[index]))
                    index++;

                wordCount++;

                // skip whitespace until next word
                while (index < text.Length && char.IsWhiteSpace(text[index]))
                    index++;
            }

            return wordCount;
        }
    }
}
