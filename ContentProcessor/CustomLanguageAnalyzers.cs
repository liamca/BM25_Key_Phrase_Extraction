using Lucene.Net.Analysis;
using Lucene.Net.Analysis.Standard;
using SF.Snowball.Ext;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSI
{
    public class CustomStandardEnglishAnalyzer : Analyzer
    {
        public override TokenStream TokenStream(string fieldName, System.IO.TextReader reader)
        {
            //create the tokenizer
            TokenStream result = new StandardTokenizer(Lucene.Net.Util.Version.LUCENE_30, reader);
            //add in filters
            result = new Lucene.Net.Analysis.Snowball.SnowballFilter(result, new EnglishStemmer());
            result = new LowerCaseFilter(result);
            result = new ASCIIFoldingFilter(result);
            result = new StopFilter(true, result, EnglishStopWords.GetEnglishStopWords());
            return result;
        }
    }

    public class CustomStandardPortugueseAnalyzer : Analyzer
    {
        public override TokenStream TokenStream(string fieldName, System.IO.TextReader reader)
        {
            //create the tokenizer
            TokenStream result = new StandardTokenizer(Lucene.Net.Util.Version.LUCENE_30, reader);
            //add in filters
            result = new Lucene.Net.Analysis.Snowball.SnowballFilter(result, new PortugueseStemmer());
            result = new LowerCaseFilter(result);
            result = new ASCIIFoldingFilter(result);
            result = new StopFilter(true, result, EnglishStopWords.GetEnglishStopWords());
            return result;
        }
    }

}
