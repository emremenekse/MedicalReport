namespace MedicalReport.Abstractions
{
    public interface IRepository
    {
        public Task<T> SaveAsync<T>(T entity, string indexName);
        public Task CreateIndexAsync(string indexName);
        public Task<List<T>> SearchAsync<T>(string query, string indexName);
    }

}
