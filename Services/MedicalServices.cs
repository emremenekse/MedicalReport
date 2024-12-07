using MedicalReport.Abstractions;
using MedicalReport.Concrete;
using MedicalReport.DTOs;
using MedicalReport.Entity;
using Nest;
using System.Collections.Generic;
using System.Reflection.Metadata;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Document = MedicalReport.Entity.Document;

namespace MedicalReport.Services
{
    public class MedicalServices
    {
        private readonly Repository _repository;
        private readonly IConfiguration _configuration;
        private readonly HttpService _httpService;
        private readonly SemaphoreSlim _semaphore = new SemaphoreSlim(1, 1);

        public MedicalServices(Repository repository, IConfiguration configuration, HttpService httpService)
        {
            _repository = repository;
            _configuration = configuration;
            _httpService = httpService;
        }
        public async Task<ResponseDTO<List<AnnotatedDocument>>> SearchAsync(string searchText)
        {
            try
            {
                var searchResults = await _repository.SearchAsync(searchText, _configuration.GetSection("Elastic")["DefaultIndex"]!);

                var extendedSearchResults = new List<AnnotatedDocument>(searchResults);
                foreach (var document in searchResults)
                {
                    string extractedText = document.Text.Length <= 100 ? document.Text : document.Text.Substring(0, 100);
                    var additionalResults = await _repository.SearchAsync(extractedText, _configuration.GetSection("Elastic")["ModifiedIndex"]!);
                    if (additionalResults != null && additionalResults.Any())
                    {
                        extendedSearchResults.Add(additionalResults.First());
                    }

                }


                return ResponseDTO<List<AnnotatedDocument>>.Success(extendedSearchResults, 200);
            }
            catch (Exception ex)
            {
                throw new Exception(ex.Message);
            }
        }

        public async Task<ResponseDTO<bool>> SaveAsync(MedicalTextDTO request)
        {
            AnnotatedDocument document = new();
            document.Text = request.Text;
            var response = await _repository.SaveAsync(document, _configuration.GetSection("Elastic")["DefaultIndex"]!);

            if(response == null)
            {
                throw new Exception("Indexleme hatası.");

            }

            return ResponseDTO<bool>.Success(201);
        }
        public async Task<ResponseDTO<bool>> IndexDocumentsAsync(string jsonFilePath)
        {
            var jsonData = await File.ReadAllTextAsync(jsonFilePath);
            var documents = JsonSerializer.Deserialize<List<AnnotatedDocument>>(jsonData);

            if (documents != null)
            {
                var tasks = documents.Select(async item =>
                {
                    await _repository.IndexDocumentAsync(item, _configuration.GetSection("Elastic")["DefaultIndex"]!);
                    var modifiedDocument = CreateModifiedDocument(item);
                    await _repository.IndexDocumentAsync(modifiedDocument, _configuration.GetSection("Elastic")["ModifiedIndex"]!);
                });

                await Task.WhenAll(tasks);
            }

            return ResponseDTO<bool>.Success(201);
        }
        public async Task<ResponseDTO<bool>> IndexDocumentsAsyncWithSource(string jsonFilePath,string source)
        {
            var jsonData = await File.ReadAllTextAsync(jsonFilePath);
            var documents = JsonSerializer.Deserialize<List<Document>>(jsonData);

            if (documents != null)
            {
                var tasks = documents.Select(async item =>
                {
                    await _repository.IndexDocumentAsync(item, source);
                    
                });

                await Task.WhenAll(tasks);
            }

            return ResponseDTO<bool>.Success(201);
        }
        private AnnotatedDocument CreateModifiedDocument(AnnotatedDocument item)
        {
            var modifications = item.Entities
                                    .Where(e => e.Class == "PERSON")
                                    .OrderBy(e => e.Start)
                                    .Select(e => new { e.Start, e.End })
                                    .ToList();

            var modifiedText = new StringBuilder();
            int previousEnd = 0;
            foreach (var mod in modifications)
            {
                if (mod.Start < item.Text.Length && mod.Start >= previousEnd)
                {
                    int length = mod.Start - previousEnd;
                    if (previousEnd + length <= item.Text.Length)
                    {
                        modifiedText.Append(item.Text.Substring(previousEnd, length));
                    }
                    modifiedText.Append("PERSON");
                    previousEnd = mod.End;
                }
            }

            if (previousEnd < item.Text.Length)
            {
                modifiedText.Append(item.Text.Substring(previousEnd));
            }

            var modifiedDocument = new AnnotatedDocument
            {
                Text = modifiedText.ToString(),
                Entities = item.Entities
            };

            return modifiedDocument;
        }




        public async Task CreateMapping()
        {
            await _repository.CreateIndexAsync(_configuration.GetSection("Elastic")["DefaultIndex"]!);
            await _repository.CreateIndexAsync(_configuration.GetSection("Elastic")["ModifiedIndex"]!);
        }

        public async Task AnonymizeAndIndexDocumentsAsync()
        {
            IEnumerable<Document>? documents = await _repository.GetDocumentsContainingPatientAsync();
            var names = await _repository.GetRandomNameAsync();
            var nameList = new List<string>();
            var random = new Random();
            var updatedDocumentsAsStringList = new List<string>(); 

            foreach (var name in names)
            {
                if (name.Tags != null && name.Tokens != null)
                {
                    string fullName = ""; 

                    for (int i = 0; i < name.Tags.Count; i++)
                    {
                        if (name.Tags[i] == 3) 
                        {
                            if (!string.IsNullOrWhiteSpace(fullName))
                            {
                                nameList.Add(fullName);
                                fullName = "";
                            }
                            fullName = name.Tokens[i]; 
                        }
                        else if (name.Tags[i] == 4) 
                        {
                            fullName += " " + name.Tokens[i]; 
                        }
                    }

                    if (!string.IsNullOrWhiteSpace(fullName)) 
                    {
                        nameList.Add(fullName); 
                    }
                }
            }

            foreach (var document in documents)
            {
                if (document.Tokens != null)
                {
                    for (int i = 0; i < document.Tokens.Count; i++)
                    {
                        if (document.Tokens[i].ToLower().Contains("patient"))
                        {
                            var randomName = nameList[random.Next(nameList.Count)];

                            document.Tokens[i] = randomName.Split(" ")[0];
                            document.Tags[i] = 333;
                        }
                    }

                    var documentAsString = JoinTokensWithPunctuation(document.Tokens);
                    updatedDocumentsAsStringList.Add(documentAsString);
                }
            }
            var testedDocuments = documents;
            await _repository.IndexDocumentsBulkAsync(documents, "cleareddata");
            var tasks = updatedDocumentsAsStringList.Select(async item =>
            {
                var jsonDocument = new { Content = item };
                string documentId = GenerateDocumentId(item);

                await _repository.IndexOrUpdateDocumentAsync(jsonDocument, "modifieddocumentnamed", documentId);
            });

            await Task.WhenAll(tasks);

        }
        private string GenerateDocumentId(string itemContent)
        {
            using var sha256 = System.Security.Cryptography.SHA256.Create();
            var hash = sha256.ComputeHash(System.Text.Encoding.UTF8.GetBytes(itemContent));
            return Convert.ToBase64String(hash);
        }
        public async Task<ResponseDTO<List<string>>> GetAllModifiedDatas()
        {
                var contentList = await _repository.GetAllContentAsync("modifieddocumentnamed");


            return ResponseDTO<List<string>>.Success(contentList, 200);
        }
        private string JoinTokensWithPunctuation(List<string> tokens)
        {
            var result = new StringBuilder();

            for (int i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];

                if (i > 0 && !IsPunctuation(token))
                {
                    result.Append(" ");
                }

                result.Append(token); 
            }

            return result.ToString();
        }

        private bool IsPunctuation(string token)
        {
            return token.Length == 1 && char.IsPunctuation(token[0]);
        }



        public async Task GetAccuracyOfModels()
        {
            string logFilePath = "output.txt";
            var clearedDocuments = await _repository.SearchTokensTagsAsync<Document>("cleareddata", q => q.MatchAll());
            var originalDocuments = clearedDocuments;

            //var willRequestStrings = await _repository.GetAllContentAsync("modifieddocumentnamed");
            List<string> updatedDocumentsAsStringList = new();
            foreach (var item in clearedDocuments)
            {
                var documentAsString = JoinTokensWithPunctuation(item.Tokens);
                updatedDocumentsAsStringList.Add(documentAsString);
            }
            
            for (int i = clearedDocuments.Count - 1; i >= 0; i--)
            {
                if (clearedDocuments[i].Tags.Count(n => n == 333) > 1)
                {
                    clearedDocuments.RemoveAt(i);
                    updatedDocumentsAsStringList.RemoveAt(i);
                }
            }

            int spacyCorrectCount = 0;
            int spacyIncorrectCount = 0;

            foreach (var (document, index) in clearedDocuments.Select((doc, i) => (doc, i)))
            {
                var requestText = updatedDocumentsAsStringList[index];
                var payload = new { text = requestText };

                // Senkron hale getirmek için 'await' doğrudan kullandık
                var result = await _httpService.PostAsync<TokenResponse, object>("http://localhost:5000/api/predict/spacy", payload);

                for (int j = 0; j < document.Tokens.Count; j++)
                {
                    var tokenText = document.Tokens[j];
                    var tokenTag = document.Tags[j];

                    if (tokenTag == 333 && char.IsUpper(tokenText[0]))
                    {
                        var matchingResponseToken = result.Body.Tokens.FirstOrDefault(t => t.Text == tokenText);
                        await WriteToFileAsync(logFilePath, $"SPACY-- İstekteki string ifade : {requestText} --Document in değerleri {tokenText} bunun tag ı {tokenTag} şimdide api isteği sonuçları Text bu {matchingResponseToken?.Text ?? "null"}  Bu da {matchingResponseToken?.EntityType ?? "null"}\n");

                        if (matchingResponseToken != null)
                        {
                            if (matchingResponseToken.EntityType == "PERSON")
                            {
                                spacyCorrectCount++;
                                break;
                            }
                            else
                            {
                                spacyIncorrectCount++;
                                break;
                            }
                        }
                        else
                        {
                            spacyIncorrectCount++;
                            break;
                        }
                    }
                }
            }

            Console.WriteLine($"Spacy Doğru Sayısı: {spacyCorrectCount}");
            Console.WriteLine($"Spacy Yanlış Sayısı: {spacyIncorrectCount}");

             int bertcorrectCount = 0;
             int bertincorrectCount = 0;

            foreach (var (document, index) in clearedDocuments.Select((doc, i) => (doc, i)))
            {
                var requestText = updatedDocumentsAsStringList[index];

                var payload = new { text = requestText };
                var result = await _httpService.PostAsync<TokenResponse, object>("http://localhost:5000/api/predict/bert", payload);

                for (int j = 0; j < document.Tokens.Count; j++)
                {
                    var tokenText = document.Tokens[j];
                    var tokenTag = document.Tags[j];

                    if (tokenTag == 333 && char.IsUpper(tokenText[0]))
                    {
                        var matchingResponseToken = result.Body.Tokens.FirstOrDefault(t => t.Text == tokenText);
                        await WriteToFileAsync(logFilePath, $"BERT -- İstekteki string ifade : {requestText} -- Document in değerleri {tokenText} bunun tag ı {tokenTag} şimdide api isteği sonuçları Text bu {matchingResponseToken?.Text ?? "null"}  Bu da {matchingResponseToken?.EntityType ?? "null"}\n");

                        if (matchingResponseToken != null)
                        {
                            if (matchingResponseToken.EntityType == "I-PER")
                            {
                                bertcorrectCount++;
                                break;
                            }
                            else
                            {
                                bertincorrectCount++;
                                break;
                            }
                        }
                        else
                        {
                            bertincorrectCount++;
                            break;
                        }
                    }
                }
            }

            Console.WriteLine($"Bert Doğru Sayısı: {bertcorrectCount}");
            Console.WriteLine($"Bert Yanlış Sayısı: {bertincorrectCount}");



             int flaircorrectCount = 0;
             int flairincorrectCount = 0;

            foreach (var (document, index) in clearedDocuments.Select((doc, i) => (doc, i)))
            {
                var requestText = updatedDocumentsAsStringList[index];

                var payload = new { text = requestText };
                var result = await _httpService.PostAsync<TokenResponse, object>("http://localhost:5000/api/predict/flair", payload);

                for (int j = 0; j < document.Tokens.Count; j++)
                {
                    var tokenText = document.Tokens[j];
                    var tokenTag = document.Tags[j];

                    if (tokenTag == 333 && char.IsUpper(tokenText[0]))
                    {
                        var matchingResponseToken = result.Body.Tokens.FirstOrDefault(t => t.Text == tokenText);
                        await WriteToFileAsync(logFilePath, $"FLAIR -- İstekteki string ifade : {requestText} -- Document in değerleri {tokenText} bunun tag ı {tokenTag} şimdide api isteği sonuçları Text bu {matchingResponseToken?.Text ?? "null"}  Bu da {matchingResponseToken?.EntityType ?? "null"}\n");

                        if (matchingResponseToken != null)
                        {
                            if (matchingResponseToken.EntityType == "PER")
                            {
                                flaircorrectCount++;
                                break;
                            }
                            else
                            {
                                flairincorrectCount++;
                                break;
                            }
                        }
                        else
                        {
                            flairincorrectCount++;
                            break;
                        }
                    }
                }
            }

            Console.WriteLine($"Flair Doğru Sayısı: {flaircorrectCount}");
            Console.WriteLine($"Flair Yanlış Sayısı: {flairincorrectCount}");



        }
        async Task WriteToFileAsync(string logFilePath, string text)
        {
            await _semaphore.WaitAsync();
            try
            {
                using (StreamWriter writer = new StreamWriter(logFilePath, append: true))
                {
                    await writer.WriteLineAsync(text);
                }
            }
            finally
            {
                _semaphore.Release();
            }
        }
    }
}
