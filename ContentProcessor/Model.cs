using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CSI
{
    public class TermVectorV2
    {
        public int docId { get; set; }
        public int wordCount { get; set; }
        public List<TermDetails> termDetails { get; set; }

    }

    public class TermDetails
    {
        public string term { get; set; }
        public int termCount { get; set; }
    }
    public class Phrase
    {
        public string originalTerm { get; set; }
        public double bm25 { get; set; }
    }
    public class Match
    {
        public int Sentence { get; set; }
        public double Total { get; set; }
    }
}
