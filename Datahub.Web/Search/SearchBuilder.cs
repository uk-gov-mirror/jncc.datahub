
using System;
using System.Collections.Generic;
using System.Linq;
using Datahub.Web.Search;
using Datahub.Web.Models;
using Nest;
using Datahub.Web.Pages.Helpers;

namespace Datahub.Web.Search
{
    public interface ISearchBuilder
    {
        List<Keyword> ParseKeywords(string[] keywords);
        SearchDescriptor<SearchResult> BuildQuery(SearchParams input);
        QueryContainer BuildDatahubQuery(string q, List<Keyword> keywords);
    }

    public class SearchBuilder : ISearchBuilder
    {
        readonly IEnv _env;
        readonly IElasticsearchService _esService;

        // the datahub only ever searches over the "datahub" site
        static readonly string ES_SITE = "datahub";

        public SearchBuilder(IEnv env, IElasticsearchService esService)
        {
            _env = env;
            _esService = esService;
        }

        public SearchDescriptor<SearchResult> BuildQuery(SearchParams input)
        {
            return new SearchDescriptor<SearchResult>()
                .Index(_env.ES_INDEX)
                .From(GetStartFromPage(input.p, input.size))
                .Size(input.size)
                .Source(src => src
                    .IncludeAll()
                    .Excludes(e => e
                        .Field(f => f.Content)
                    )
                )
                .Query(l => BuildDatahubQuery(input.q, ParseKeywords(input.k)))
                .Highlight(h => h
                    .Fields(f => f.Field(x => x.Content)
                                    .Type(HighlighterType.Fvh)
                                    .Order(HighlighterOrder.Score)
                                    .NumberOfFragments(1),
                            f => f.Field(x => x.Title)
                    )
                    .PreTags("<b>")
                    .PostTags("</b>")
                );
        }

        public List<Keyword> ParseKeywords(string[] keywords)
        {
            return keywords.Select(k =>
            {
                int lastIndexOfSlash = k.LastIndexOf('/');
                if (lastIndexOfSlash > 0)
                {
                    // has a slash, so assume this keyword this has a vocab
                    string vocab = k.Substring(0, lastIndexOfSlash);
                    string value = k.Substring(lastIndexOfSlash + 1);
                    return new Keyword { Vocab = vocab, Value = value };
                }
                else
                {
                    return new Keyword { Vocab = null, Value = k };
                }
            }).ToList();
        }

        public int GetStartFromPage(int page, int size)
        {
            return (page - 1) * size;
        }

        public QueryContainer BuildDatahubQuery(string q, List<Keyword> keywords)
        {
            QueryContainer fullTextcontainer;
            QueryContainer keywordSearch = new QueryContainer();

            /**
             * Full Text Search Logic
             * 
             * Use a bool query, `Filter` on the site first (reduce search area), then match on
             * `Should`, entires in `Should` are searches on title and content, at least one of
             * these should match (MinimumShouldMatch = 1)
             *
             * If we have no string to search on we convert the Bool Query to a single MatchQuery
             * matching on the Site (identical to the initial `Filter` query)
             */
            if (q.IsNotBlank())
            {
                fullTextcontainer = new BoolQuery()
                {
                    Filter = new QueryContainer[]
                    {
                        new MatchQuery { Field = "site", Query = ES_SITE }
                    },
                    Should = new QueryContainer[]
                    {
                        new CommonTermsQuery() {
                            Field = "content",
                            Query = q,
                            CutoffFrequency = 0.001,
                            LowFrequencyOperator = Operator.Or
                        },
                        new CommonTermsQuery()
                        {
                            Field = "title",
                            Query = q,
                            CutoffFrequency = 0.001,
                            LowFrequencyOperator = Operator.Or
                        }
                    },
                    MinimumShouldMatch = 1
                };
            }
            else
            {
                // If we have no text search then make sure we are only matching on 
                // the correct site
                fullTextcontainer = new MatchQuery { Field = "site", Query = ES_SITE };
            }

            if (keywords.Any())
            {
                // TODO: check this logic even works for multiple queries, suspect it doesn't really 
                // for each keyword add a new query container containing a must match pair

                /**
                 * Keyword search logic, each vocab/value pair is unique and needs to be queried as 
                 * one, so and each individual BoolQuery together into a single container
                 */
                foreach (Keyword keyword in keywords)
                {
                    keywordSearch = keywordSearch && new BoolQuery
                    {
                        Must = new QueryContainer[]
                        {
                            new MatchQuery { Field = "keywords.vocab", Query = keyword.Vocab },
                            new MatchQuery { Field = "keywords.value", Query = keyword.Value }
                        }
                    };
                }
            }

            /**
             * Use some predicate logic to turn the Keyword Search into a `Filter` for the main
             * search
             */
            return fullTextcontainer && +keywordSearch;
        }
    }
}
