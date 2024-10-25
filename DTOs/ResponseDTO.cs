using System.Text.Json.Serialization;

namespace MedicalReport.DTOs
{
    public class ResponseDTO<T>
    {
        public T Data { get; private set; }
        [JsonIgnore]
        public int StatusCode { get; private set; }
        [JsonIgnore]
        public bool IsSuccessful { get; private set; }
        public string ErrorMessage { get; private set; }
        public static ResponseDTO<T> Success(T data, int statusCode)
        {
            return new ResponseDTO<T> { Data = data, StatusCode = statusCode, IsSuccessful = true };
        }

        public static ResponseDTO<T> Success(int statusCode)
        {
            return new ResponseDTO<T> { Data = default, StatusCode = statusCode, IsSuccessful = true };
        }
        public static ResponseDTO<T> Fail(string errorMessage, int statusCode)
        {
            return new ResponseDTO<T> { ErrorMessage = errorMessage, StatusCode = statusCode, IsSuccessful = false };
        }
    }
}
