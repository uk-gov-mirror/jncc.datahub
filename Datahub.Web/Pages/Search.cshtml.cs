using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Datahub.Web.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Datahub.Web.Elasticsearch;
using Nest;
using Microsoft.AspNetCore.Mvc;

namespace Datahub.Web.Pages
{
    public class SearchModel : PageModel
    {
        public const string DefaultIndex = "main";
        public const string DefaultSite = "datahub";
        public const int DefaultSize = 10;
        public const int DefaultStart = 0;

        private readonly IHostingEnvironment _env;
        private readonly ElasticClient _client;

        public ISearchResponse<SearchResult> Results { get; set; }

        [BindProperty(Name = "q", SupportsGet = true)]
        public string QueryString{ get; set; }
        [BindProperty(Name = "p", SupportsGet = true)]
        public int CurrentPage { get; set; }
        [BindProperty(Name = "s", SupportsGet = true)]
        public int CurrentPageSize { get; set; }

        public List<Keyword> Keywords { get; set; }

        public SearchModel(IHostingEnvironment env, IElasticsearchService elasticsearchService)
        {
            _env = env;
            _client = elasticsearchService.Client();
        }
        
        public async Task OnGetAsync(string q, string[] k, int p = 1, int size = DefaultSize)
        {
            CurrentPageSize = size;
            CurrentPage = p;
            Keywords = ParseKeywords(k);

            if (!string.IsNullOrWhiteSpace(q))
            {
                Results = _client.Search<SearchResult>(s => s
                    .Index(DefaultIndex)
                    .From(ElasticsearchService.GetStartFromPage(p, size))
                    .Size(size)
                    .Source(src => src
                        .IncludeAll()
                        .Excludes(e => e
                            .Field(f => f.Content)
                        )
                    )
                    .Query(l => ElasticsearchService.BuildDatahubQuery(q, ParseKeywords(k), DefaultSite))
                    .Highlight(h => h
                        .Fields(f => f.Field(x => x.Content))
                        .PreTags("<b>")
                        .PostTags("</b>")
                    )
                );
            }
        }

        private List<Keyword> ParseKeywords(string[] keywords)
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

        private IEnumerable<Asset> ApplyQuery(IEnumerable<Asset> assets)
        {
            return assets.Where(a => a.Metadata.Keywords.Any(k =>
                Keywords.Any(kx => kx.Vocab == k.Vocab && kx.Value == k.Value)
                ));
        }
    }
}
