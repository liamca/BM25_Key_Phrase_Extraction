# Key Phrase Extraction and Text Summarization using Azure Search and BM25

The purpose of this project is to show an alternate method for extracting key terms and phrases (uni-grams & bi-grams) from a set of unstructred text files. The resulting data can be used in Azure Search applications to allow you to more effectively explore this unstructured data.  

Here is an example of a results that leverages [News, Healthcare, Legal and Jobs datasets](http://documentsearch.azurewebsites.net/).

## Key Phrase Extraction using Full Corpus vs API's

There are numerous API's that allow you to provide the ability to both extract key phrases and generate summaries over a set of text.  These API's are extremely simply, albeit that they can be costly for large data sets.  The biggest issue with these API's is that they have been trained against datasets that may very well not be related to your content.  For example, if you have a medical dataset, and the API was trained using words from WikiPedia, the terms that are important in your content might not be the same as what was found from WikiPedia.

Using the [BM25 algorithm](https://en.wikipedia.org/wiki/Okapi_BM25), we can scan your entire dataset to identify what is defined as the most important terms.  BM25, has been used for quite some time in search engines to help identify important content, so leveraging this as a method for key phrase extraction has been well proven.

## Assumptions

This project assumes that you have extracted your content into text files.  It is very likely your content will be in PDF's, Office or other document types.  There are numerous techniques for doing this including Apache Tika.  If you are interested in doing this using Azure Function, I have some [samples here](https://github.com/liamca/AzureSearch-AzureFunctions-CognitiveServices).

## Language Support

Currently, this processor supports English and Portuguese, however, you can cery easily add support for other languages by adding a new set of stop words for the language you are interested.  Kaggle, has a nice starting [set of stop words](https://www.kaggle.com/nltkdata/stopwords/data) for different languages.

To do this, create a new class for this language as you [see here](https://github.com/liamca/BM25_Key_Phrase_Extraction/tree/master/ContentProcessor/StopWords) and then in the code where it refers to CustomStandardPortugueseAnalyzer, add a reference to this new stop word class you added.

## Performance and Scale

This processing is done purely on a single machine.  It has done really well for 100's of thousands of files and even millions of smaller documents (1-2KB), but if your content is larger, you will want to look at how to parallelize this using something like Spark or Azure Data Lake Analytics.
