using Elasticsearch.Net;
using Nest;

namespace MedicalReport.ElasticSearchExtensions
{
    public static class ElasticSearchExtensions
    {
        public static void AddElasticsearch(this IServiceCollection services, IConfiguration configuration)
        {
            var options = configuration.GetSection("Elastic").Get<ElasticsearchOptions>();
            var pool = new SingleNodeConnectionPool(options!.Url);
            var settings = new ConnectionSettings(pool);
            var client = new ElasticClient(settings);

            services.AddSingleton(client);
        }
    }

    public class ElasticsearchOptions
    {
        public Uri Url { get; set; } = null!;
        public string DefaultIndex { get; set; } = null!;
        public string Username { get; set; } = null!;
        public string Password { get; set; } = null!;
    }
}
