﻿using MedicalReport.Concrete;
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
        public MedicalServices(Repository repository, IConfiguration configuration)
        {
            _repository = repository;
            _configuration = configuration;
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
            var documents = await _repository.GetDocumentsContainingPatientAsync();
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

                            document.Tokens[i] = randomName;
                        }
                    }

                    var documentAsString = JoinTokensWithPunctuation(document.Tokens);
                    updatedDocumentsAsStringList.Add(documentAsString);
                }
            }

            var tasks = updatedDocumentsAsStringList.Select(async item =>
            {
                var jsonDocument = new { Content = item }; 
                await _repository.IndexDocumentAsync(jsonDocument, "modifieddocumentnamed");
            });

            await Task.WhenAll(tasks);

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
    }
}
