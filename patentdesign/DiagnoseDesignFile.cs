// Quick diagnostic script to check file F/DS/NT/O/2026/6687
// Run this as a standalone console app or integrate into your existing services

using MongoDB.Driver;
using System.Security.Authentication;

namespace patentdesign.Diagnostics;

public class DiagnoseDesignFile
{
    public static async Task CheckFile(string fileId = "F/DS/NT/O/2026/6687")
    {
        // Replace with your actual connection string
        var connectionString = "YOUR_CONNECTION_STRING";
        var databaseName = "YOUR_DATABASE_NAME";
        var collectionName = "files"; // or your actual collection name

        MongoClientSettings settings = MongoClientSettings.FromUrl(new MongoUrl(connectionString));
        settings.SslSettings = new SslSettings() { EnabledSslProtocols = SslProtocols.Tls12 };
        
        var mongoClient = new MongoClient(settings);
        var database = mongoClient.GetDatabase(databaseName);
        var collection = database.GetCollection<dynamic>(collectionName);

        Console.WriteLine($"=== Diagnosing File: {fileId} ===\n");

        var file = await collection.Find(Builders<dynamic>.Filter.Eq("FileId", fileId)).FirstOrDefaultAsync();

        if (file == null)
        {
            Console.WriteLine($"❌ File NOT FOUND in database: {fileId}");
            return;
        }

        Console.WriteLine($"✅ File Found!");
        Console.WriteLine($"   ID: {file._id}");
        Console.WriteLine($"   Type: {file.Type}");
        
        if (file.TitleOfDesign != null)
            Console.WriteLine($"   Title: {file.TitleOfDesign}");

        Console.WriteLine($"\n=== Attachments Analysis ===");

        if (file.Attachments == null)
        {
            Console.WriteLine("❌ No Attachments field found!");
            return;
        }

        var attachments = (IEnumerable<dynamic>)file.Attachments;
        var attachmentsList = attachments.ToList();

        Console.WriteLine($"Total Attachments: {attachmentsList.Count}");

        foreach (var att in attachmentsList)
        {
            Console.WriteLine($"\n  - Name: {att.name}");
            
            if (att.url != null)
            {
                var urls = (IEnumerable<dynamic>)att.url;
                var urlList = urls.Select(u => u.ToString()).ToList();
                Console.WriteLine($"    URL Count: {urlList.Count}");
                
                foreach (var url in urlList)
                {
                    Console.WriteLine($"    • {url}");
                }
            }
            else
            {
                Console.WriteLine($"    URL: NULL");
            }
        }

        // Check specifically for "designs" attachment
        var designAttachment = attachmentsList.FirstOrDefault(a => a.name == "designs");

        if (designAttachment == null)
        {
            Console.WriteLine($"\n❌ NO 'designs' ATTACHMENT FOUND!");
            Console.WriteLine($"   Available attachment names: {string.Join(", ", attachmentsList.Select(a => a.name.ToString()))}");
            return;
        }

        Console.WriteLine($"\n=== Design Images Check ===");

        if (designAttachment.url == null)
        {
            Console.WriteLine("❌ Design attachment exists but URL list is NULL!");
            return;
        }

        var designUrls = ((IEnumerable<dynamic>)designAttachment.url).Select(u => u.ToString()).ToList();

        Console.WriteLine($"Found {designUrls.Count} design image URL(s):");

        using var httpClient = new HttpClient();
        httpClient.Timeout = TimeSpan.FromSeconds(10);

        for (int i = 0; i < designUrls.Count; i++)
        {
            var url = designUrls[i];
            Console.WriteLine($"\n[{i + 1}] {url}");

            if (string.IsNullOrWhiteSpace(url))
            {
                Console.WriteLine("    ⚠️  EMPTY/WHITESPACE URL");
                continue;
            }

            if (url.Equals("NULL", StringComparison.OrdinalIgnoreCase))
            {
                Console.WriteLine("    ⚠️  URL is the string 'NULL'");
                continue;
            }

            try
            {
                var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);
                
                if (response.IsSuccessStatusCode)
                {
                    Console.WriteLine($"    ✅ ACCESSIBLE (HTTP {(int)response.StatusCode})");
                    
                    if (response.Content.Headers.ContentLength.HasValue)
                    {
                        var sizeKB = response.Content.Headers.ContentLength.Value / 1024.0;
                        Console.WriteLine($"    Size: {sizeKB:N2} KB");
                    }
                }
                else
                {
                    Console.WriteLine($"    ❌ NOT ACCESSIBLE - HTTP {(int)response.StatusCode}: {response.ReasonPhrase}");
                }
            }
            catch (HttpRequestException ex)
            {
                Console.WriteLine($"    ❌ NETWORK ERROR: {ex.Message}");
            }
            catch (TaskCanceledException)
            {
                Console.WriteLine($"    ❌ TIMEOUT - Request took too long");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"    ❌ ERROR: {ex.Message}");
            }
        }

        Console.WriteLine($"\n=== Summary ===");
        Console.WriteLine($"File: {fileId}");
        Console.WriteLine($"Design attachment exists: {designAttachment != null}");
        Console.WriteLine($"Total URLs: {designUrls.Count}");
        Console.WriteLine($"Empty/NULL URLs: {designUrls.Count(u => string.IsNullOrWhiteSpace(u) || u.Equals("NULL", StringComparison.OrdinalIgnoreCase))}");
        Console.WriteLine($"\nThis file {(designUrls.Any(u => !string.IsNullOrWhiteSpace(u) && !u.Equals("NULL", StringComparison.OrdinalIgnoreCase)) ? "SHOULD" : "WILL NOT")} show images in the acknowledgement letter.");
    }
}
