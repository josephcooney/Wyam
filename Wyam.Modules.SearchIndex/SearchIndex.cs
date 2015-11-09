﻿using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Wyam.Common.Documents;
using Wyam.Common.IO;
using Wyam.Common.Modules;
using Wyam.Common.Pipelines;

namespace Wyam.Modules.SearchIndex
{
    public class SearchIndex : IModule
    {
        private string _stopwordsFilename;
        private static readonly Regex StripHtmlAndSpecialChars = new Regex(@"<[^>]+>|&[a-z]{2,};|&#\d+;|[^a-z-#]", RegexOptions.Compiled);
        private bool _enableStemming;

        public SearchIndex(string stopwordsFilename = null, bool enableStemming = false)
        {
            _stopwordsFilename = stopwordsFilename;
            _enableStemming = enableStemming;
        }

        public IEnumerable<IDocument> Execute(IReadOnlyList<IDocument> inputs, IExecutionContext context)
        {
            SearchIndexItem[] searchIndexItems = context.Documents.Where(f => f.ContainsKey(MetadataKeys.SearchIndexItem)).Select(f => f[MetadataKeys.SearchIndexItem]).OfType<SearchIndexItem>().ToArray();

            if( searchIndexItems.Length == 0 )
            {
                context.Trace.Warning("It's not possible to build the search index, because no documents contain the meta data 'SearchIndexItem'.");
                return inputs;
            }
            
            string[] stopwords = GetStopwords(context);
            string jsFileContent = BuildSearchIndex(searchIndexItems, stopwords);
            IDocument searchIndexDocument = context.GetNewDocument(jsFileContent);

            return inputs.Concat(new []{ searchIndexDocument });
        }
        
        private string BuildSearchIndex(IEnumerable<SearchIndexItem> searchIndexItems, string[] stopwords)
        {
            StringBuilder sb = new StringBuilder();

            for (int i = 0; i < searchIndexItems.Count(); ++i)
            {
                SearchIndexItem itm = searchIndexItems.ElementAt(i);
                sb.AppendLine($@"a({{
id:{i},
title:{CleanString(itm.Title, stopwords)},
content:{CleanString(itm.Content, stopwords)},
description:{CleanString(itm.Description, stopwords)},
tags:'{itm.Tags}'
}});");
            }

            foreach (SearchIndexItem itm in searchIndexItems)
            {
                sb.AppendLine($@"y({{url:'{PathHelper.ToLink(itm.Url)}',title:{ToJsonString(itm.Title)},description:{ToJsonString(itm.Description)}}});");
            }

            return CreateJs(sb.ToString());
        }

        private string CreateJs(string dynamicJsContent)
        {
            return @"var searchModule = function() {
var idMap = [];
function y(e){idMap.push(e);}
var idx = lunr(function() {
this.field('title', { boost: 10})
this.field('content')
this.field('description', { boost: 5})
this.field('tags', { boost: 50})
this.ref('id')

this.pipeline.remove(lunr.stopWordFilter);" + (_enableStemming ? "this.pipeline.remove(lunr.stemmer);" : "") + @"
})
function a(e){idx.add(e);}

" + dynamicJsContent + @"
return {
search: function(q) {return idx.search(q).map(function(i){return idMap[i.ref];});}
};
}();";
        }

        private static string CleanString(string input, string[] stopwords)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return "''";
            }

            string clean = input.ToLowerInvariant();
            clean = StripHtmlAndSpecialChars.Replace(clean, " ").Trim();
            clean = Regex.Replace(clean, @"\s{2,}", " ");
            clean = string.Join(" ", clean.Split(' ').Where(f => f.Length > 1 && !stopwords.Contains(f)).ToArray());
            clean = ToJsonString(clean);

            return clean;
        }

        private static string ToJsonString(string s)
        {
            return Newtonsoft.Json.JsonConvert.ToString(s);
        }

        private string[] GetStopwords(IExecutionContext context)
        {
            string[] stopwords = new string[0];

            if (!string.IsNullOrWhiteSpace(_stopwordsFilename))
            {
                string fullStopwordsFilename = Path.Combine(context.InputFolder, _stopwordsFilename);

                if (File.Exists(fullStopwordsFilename))
                {
                    stopwords = File.ReadAllLines(fullStopwordsFilename).Select(f => f.Trim().ToLowerInvariant()).Where(f => f.Length > 1).ToArray();
                }
            }

            return stopwords;
        }
    }
}