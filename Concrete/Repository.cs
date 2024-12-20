﻿
using MedicalReport.Entity;
using Nest;

namespace MedicalReport.Concrete
{
    public class Repository
    {
        private readonly ElasticClient _client;

        public Repository(ElasticClient client)
        {
            _client = client;
        }
        public async Task<List<T>> SearchTokensTagsAsync<T>(string indexName, Func<QueryContainerDescriptor<T>, QueryContainer> query, int size = 1000) where T : class
        {
            var response = await _client.SearchAsync<T>(s => s
                .Index(indexName)
                .Size(size)
                .Query(query)
            );

            if (!response.IsValid)
            {
                throw new Exception($"Elasticsearch sorgusu başarısız oldu: {response.ServerError?.Error.Reason}");
            }

            return response.Documents.ToList();
        }

        public async Task<List<AnnotatedDocument>> SearchAsync(string query, string indexName)
        {
            var response = await _client.SearchAsync<AnnotatedDocument>(s => s
                .Index(indexName)
                .Query(q => q
                    .Match(m => m
                        .Field(f => f.Text)
                        .Query(query)
                    )
                )
            );

            if (!response.IsValid)
            {
                throw new Exception("Arama sırasında bir hata oluştu: " + response.ServerError?.Error.Reason);
            }

            return response.Documents.ToList();
        }
        public async Task<List<string>> GetAllContentAsync(string indexName)
        {
            var response = await _client.SearchAsync<dynamic>(s => s
                .Index(indexName)
                .Size(1000)
                .Query(q => q.MatchAll())
            );

            if (!response.IsValid)
            {
                throw new Exception("Veri alımı sırasında bir hata oluştu: " + response.ServerError?.Error.Reason);
            }

            var contentList = response.Hits
                .Select(hit => hit.Source as IDictionary<string, object>)
                .Where(source => source != null &&
                                 source.Keys.Any(key => key.Equals("content", StringComparison.OrdinalIgnoreCase)))
                .Select(source => source.First(kv => kv.Key.Equals("content", StringComparison.OrdinalIgnoreCase)).Value.ToString())
                .ToList();

            return contentList;
        }
        public async Task IndexOrUpdateDocumentAsync<T>(T document, string indexName, string documentId) where T : class
        {
            var response = await _client.IndexAsync(document, idx => idx
                .Index(indexName)
                .Id(documentId) 
            );

            if (!response.IsValid)
            {
                throw new Exception("Veri indexlenemedi: " + response.ServerError?.Error.Reason);
            }
        }

        public async Task IndexDocumentsBulkAsync<T>(IEnumerable<T> documents, string indexName) where T : class
        {
            try
            {
                var bulkDescriptor = new BulkDescriptor();

                foreach (var document in documents)
                {
                    bulkDescriptor.Index<T>(op => op
                        .Index(indexName)
                        .Document(document));
                }

                var response = await _client.BulkAsync(bulkDescriptor);

                if (!response.IsValid)
                {
                    throw new Exception("Toplu indeksleme başarısız oldu: " + response.ServerError?.Error.Reason);
                }
            }
            catch (Exception ex)
            {

                Console.WriteLine(ex.Message);
            }
            
        }

        public async Task<T> SaveAsync<T>(T entity, string indexName) where T : class
        {
            var response = await _client.IndexAsync(entity, idx => idx.Index(indexName));
            if (response.IsValid)
            {
                return entity;
            }
            else
            {
                throw new Exception("Veri indexlenemedi: " + response.ServerError?.Error.Reason);
            }
        }
        public async Task IndexDocumentAsync<T>(T document, string indexName) where T : class
        {
            var response = await _client.IndexAsync(document, idx => idx.Index(indexName));
            if (!response.IsValid)
            {
                throw new Exception("Veri indexlenemedi: " + response.ServerError?.Error.Reason);
            }
        }
        public async Task CreateIndexAsyncDocument(string indexName)
        {
            var createIndexResponse = await _client.Indices.CreateAsync(indexName, c => c
        .Map<Document>(m => m
            .AutoMap() 
            .Properties(ps => ps
                .Text(t => t
                    .Name(n => n.Tokens)) 
                .Number(num => num
                    .Name(n => n.Tags) 
                    .Type(NumberType.Integer))
            )
        )
    );

            if (!createIndexResponse.IsValid)
            {
                throw new Exception($"Index creation failed: {createIndexResponse.DebugInformation}");
            }
        }
        public async Task CreateIndexAsync(string indexName)
        {
            var createIndexResponse = await _client.Indices.CreateAsync(indexName, c => c
                .Map<AnnotatedDocument>(m => m
                    .AutoMap()
                    .Properties(ps => ps
                        .Text(s => s
                            .Name(n => n.Text))
                        .Nested<MedicalReport.Entity.Entity>(n => n
                            .Name(nn => nn.Entities)
                            .AutoMap()
                            .Properties(np => np
                                .Keyword(k => k
                                    .Name(nk => nk.Class))
                                .Number(num => num
                                    .Name(nu => nu.Start)
                                    .Type(NumberType.Integer))
                                .Number(num => num
                                    .Name(nu => nu.End)
                                    .Type(NumberType.Integer))
                            )
                        )
                    )
                )
            );

            if (!createIndexResponse.IsValid)
            {
                throw new Exception($"Index creation failed: {createIndexResponse.DebugInformation}");
            }
        }

        public async Task<IEnumerable<Document>> GetDocumentsContainingPatientAsync()
        {
            var response = await _client.SearchAsync<Document>(s => s
    .Index("bc5cdr_data")
    .Size(1000)
    .Query(q => q
        .Term(t => t
            .Field(f => f.Tokens)
            .Value("patient") 
        )
    )
);
            List<Document> filteredDocuments = new();
            response.Documents.Where(doc =>
            {
                var tokens = doc.Tokens?.Select(t => t.ToLower()).ToList();
                if (tokens == null || tokens.Count == 0)
                {
                    return false; 
                }

                for (int i = 0; i < tokens.Count; i++)
                {
                    if (tokens[i] == "patient")
                    {
                        if (i > 0)
                        {
                            var previousToken = tokens[i - 1];
                            if (previousToken.ToLower() != "every" && previousToken.ToLower() != "in" &&
                            previousToken.ToLower() != "the" && previousToken.ToLower() != "a" &&
                            previousToken.ToLower() != "1" &&
                            previousToken.ToLower() != "per" &&
                            previousToken.ToLower() != "this" &&
                            previousToken.ToLower() != "one" && previousToken.ToLower() != "ın" && previousToken.ToLower() != "each")
                            {
                                filteredDocuments.Add(doc); 
                                return true; 
                            }
                        }
                        else
                        {
                            
                            filteredDocuments.Add(doc);
                            return true;
                        }
                    }
                }

                return false; 
            }).ToList();

            return filteredDocuments;
        }

        public async Task<IEnumerable<Document>> GetRandomNameAsync()
        {
            var response = await _client.SearchAsync<Document>(s => s
                .Index("conll2003_data")
                .Size(1000)
                .Query(q => q
                    .Bool(b => b
                        .Filter(f => f
                            .Terms(t => t
                                .Field(f => f.Tags) 
                                .Terms(3, 4)
                            )
                        )
                    )
                )
            );

            return response.Documents;
        }

        public async Task IndexAnonymizedDocumentAsync(Document document)
        {
            var response = await _client.IndexDocumentAsync(document);

            if (!response.IsValid)
            {
                throw new Exception($"Indexing failed: {response.OriginalException.Message}");
            }
        }
    }
}
