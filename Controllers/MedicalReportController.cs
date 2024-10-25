using MedicalReport.DTOs;
using MedicalReport.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using System.Text.Json;

namespace MedicalReport.Controllers
{
    [Route("api/[controller]/[action]")]
    [ApiController]
    public class MedicalReportController: CustomBaseController
    {
        private readonly MedicalServices _medicalServices;

        public MedicalReportController(MedicalServices medicalServices)
        {
            _medicalServices = medicalServices;
        }
        [HttpGet]
        public async Task<IActionResult> SearchAsync([FromQuery] string searchText)
        {
            var response = await _medicalServices.SearchAsync(searchText);
            return CreateActionResultInstance(response);
        }
        [HttpPost]
        public async Task<IActionResult> SaveOrUpdateDocument(MedicalTextDTO request)
        {
                var response = await _medicalServices.SaveAsync(request);
                return CreateActionResultInstance(response);
        }
        [HttpPost]
        public async Task<IActionResult> IndexDocument(string jsonFilePath)
        {
            var response = await _medicalServices.IndexDocumentsAsync(jsonFilePath);
            return CreateActionResultInstance(response);
        }
        [HttpPost]
        public async Task<IActionResult> IndexDocumentWithSource(string jsonFilePath,string source)
        {
            var response = await _medicalServices.IndexDocumentsAsyncWithSource(jsonFilePath, source);
            return CreateActionResultInstance(response);
        }
        [HttpGet]
        public async Task<IActionResult> AnonymizeAndIndexDocumentsAsync()
        {
            //var response = await _medicalServices.AnonymizeAndIndexDocumentsAsync();
            await _medicalServices.AnonymizeAndIndexDocumentsAsync();
            return Ok();
        }
        [HttpGet]
        public async Task<IActionResult> GetAllModifiedDocumentsAsync()
        {
            var response = await _medicalServices.GetAllModifiedDatas();

            return CreateActionResultInstance(response); 
        }


        [HttpPost]
        public async Task CreateMapping()
        {
             await _medicalServices.CreateMapping();
        }


        //[HttpPost("upload")]
        //public async Task<IActionResult> UploadDataset([FromForm] IFormFile file, [FromQuery] string source)
        //{
        //    if (file == null || file.Length == 0 || string.IsNullOrEmpty(source))
        //        return BadRequest("Invalid file or source.");

        //    using var stream = new StreamReader(file.OpenReadStream());
        //    var jsonData = await stream.ReadToEndAsync();

        //    var records = JsonSerializer.Deserialize<List<Document>>(jsonData);
        //    foreach (var record in records)
        //    {
        //        record.Source = source;  // Kaynağı ayarlıyoruz
        //        var response = await _elasticClient.IndexDocumentAsync(record);

        //        if (!response.IsValid)
        //        {
        //            return StatusCode(500, $"Failed to index document: {response.DebugInformation}");
        //        }
        //    }

        //    return Ok("Dataset uploaded and indexed successfully.");
        //}
        
    }
}
