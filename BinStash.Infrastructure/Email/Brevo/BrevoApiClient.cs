// Copyright (C) 2025  Lukas Eßmann
// 
//      This program is free software: you can redistribute it and/or modify
//      it under the terms of the GNU Affero General Public License as published
//      by the Free Software Foundation, either version 3 of the License, or
//      (at your option) any later version.
// 
//      This program is distributed in the hope that it will be useful,
//      but WITHOUT ANY WARRANTY; without even the implied warranty of
//      MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE.  See the
//      GNU Affero General Public License for more details.
// 
//      You should have received a copy of the GNU Affero General Public License
//      along with this program.  If not, see <https://www.gnu.org/licenses/>.

using System.Net.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace BinStash.Infrastructure.Email.Brevo;

public class BrevoApiClient
{
    private readonly ILogger<BrevoApiClient> _logger;
    private readonly HttpClient _httpClient;

    public BrevoApiClient(IConfiguration configuration, ILogger<BrevoApiClient> logger, HttpClient httpClient)
    {
        _logger = logger;
        _httpClient = httpClient;

        var apiKey = configuration["Email:Brevo:ApiKey"];
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new InvalidOperationException("Brevo API key is not configured.");
        }
        
        _httpClient.DefaultRequestHeaders.Add("api-key", apiKey);
        _httpClient.BaseAddress = new Uri("https://api.brevo.com/v3/");
    }
    
    public async Task SendEmailAsync(string senderName, string senderEmail, string toEmail, string subject, string htmlContent, string replyTo)
    {
        var payload = new
        {
            htmlContent,
            sender = new
            {
                name = senderName,
                email = senderEmail
            },
            subject,
            to = new[]
            {
                new
                {
                    email = toEmail
                }
            }
        };

        var response = await _httpClient.PostAsJsonAsync("smtp/email", payload);
        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Failed to send email via Brevo. Status Code: {StatusCode}, Response: {Response}",
                response.StatusCode, errorContent);
            throw new InvalidOperationException("Failed to send email via Brevo.");
        }
        var responseContent = await response.Content.ReadFromJsonAsync<object>();
        _logger.LogInformation("Email sent to {ToEmail} via Brevo. Result: {@MessageId}", toEmail, responseContent);
    }
}