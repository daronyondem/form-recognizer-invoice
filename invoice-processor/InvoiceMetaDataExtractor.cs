using System;
using System.IO;
using Microsoft.Azure.WebJobs;
using Microsoft.Azure.WebJobs.Host;
using Microsoft.Extensions.Logging;
using Azure;
using Azure.AI.FormRecognizer.DocumentAnalysis;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using static Microsoft.Azure.WebJobs.Host.Bindings.OpenType;
using System.Text.Json;

namespace invoice_processor
{
    public class InvoiceMetaDataExtractor
    {
        /// <summary>
        /// Required fields to be extracted.
        /// - Invoice Number
        /// - Physical Address
        /// For Electricity
        /// - Start Reading - End Reading
        /// - Actual Reading
        /// - Daily Average Consumption
        /// </summary>    
        /// TODO: Proper logging and exception handling implementations are missing.

        static string endpoint = System.Environment.GetEnvironmentVariable("FORM_RECOGNIZER_ENDPOINT", EnvironmentVariableTarget.Process);
        static string key = System.Environment.GetEnvironmentVariable("FORM_RECOGNIZER_KEY", EnvironmentVariableTarget.Process);
        static AzureKeyCredential credential = new AzureKeyCredential(key);
        static DocumentAnalysisClient client = new DocumentAnalysisClient(new Uri(endpoint), credential);

        [FunctionName("InvoiceMetaDataExtractor")]
        public static async Task Run([BlobTrigger("invoices/{blobName}.{blobExtension}", Connection = "InvoiceStorage")] Stream myBlob, 
            [Blob("invoicemetadata/{blobName}.json", FileAccess.Write, Connection = "InvoiceStorage")] Stream invoiceMetadata, ILogger log)
        {
            AnalyzeDocumentOperation operation = await client.AnalyzeDocumentAsync(WaitUntil.Completed, "prebuilt-invoice", myBlob);

            AnalyzeResult result = operation.Value;
            Invoice modelInvoice = new();

            // Getting InvoiceID and CustomerAddress out.
            for (int i = 0; i < result.Documents.Count; i++)
            {
                AnalyzedDocument document = result.Documents[i];

                if (document.Fields.TryGetValue("InvoiceId", out DocumentField? invoiceIdField))
                {
                    if (invoiceIdField.FieldType == DocumentFieldType.String)
                    {
                        modelInvoice.InvoiceNumber = invoiceIdField.Value.AsString();
                    }
                }

                if (document.Fields.TryGetValue("CustomerAddress", out DocumentField? customerAddressField))
                {
                    if (customerAddressField.FieldType == DocumentFieldType.Address)
                    {
                        modelInvoice.PhysicalAddress = customerAddressField.Content;
                    }
                }
            }
            for (int i = 0; i < result.Tables.Count; i++)
            {
                DocumentTable table = result.Tables[i];

                if (table.BoundingRegions.Count > 0)
                {
                    if (table.BoundingRegions[0].PageNumber == 2)
                    {
                        if ((from inc in table.Cells where inc.Kind == DocumentTableCellKind.ColumnHeader && inc.Content.Contains("Electricity") select inc).Any())
                        {
                            modelInvoice.Electricity = new Invoice.ElectricityDetails();
                            ExtractElectricityMeterReadings(modelInvoice.Electricity,
                                (from inc in table.Cells where inc.RowIndex == 2 && inc.ColumnIndex == 0 select inc.Content).FirstOrDefault());
                            ExtractElectricityDailyAverage(modelInvoice.Electricity,
                                (from inc in table.Cells where inc.RowIndex == 3 && inc.ColumnIndex == 0 select inc.Content).FirstOrDefault());
                        }
                    }
                }
            }

            var json = JsonSerializer.Serialize(modelInvoice);
            using (var writer = new StreamWriter(invoiceMetadata))
            {
                await writer.WriteAsync(json);
            }
        }

        public static void ExtractElectricityMeterReadings(Invoice.ElectricityDetails ElectricityDetails, string text)
        {
            // Define a regex pattern to match the start and end readings.
            string pattern = @"start reading\s+([\d,\.]+).*end reading\s+([\d,\.]+).*=\s+([\d,\.]+)\s+kWh\s+-\s+Actual Reading";

            // Match the pattern against the input text.
            Match match = Regex.Match(text, pattern);

            // Extract the start reading, end reading, and actual reading from the match.
            ElectricityDetails.StartReading = double.Parse(match.Groups[1].Value.Replace(",", ""));
            ElectricityDetails.EndReading = double.Parse(match.Groups[2].Value.Replace(",", ""));
            ElectricityDetails.ActualReading = double.Parse(match.Groups[3].Value.Replace(",", ""));
        }

        public static void ExtractElectricityDailyAverage(Invoice.ElectricityDetails ElectricityDetails, string text)
        {
            // Define a regex pattern to match the start and end readings.
            string pattern = @"Daily average consumption\s+([\d,\.]+)\s+kWh";

            // Match the pattern against the input text.
            Match match = Regex.Match(text, pattern);

            // Extract the start reading, end reading, and actual reading from the match.
            ElectricityDetails.DailyAverageConsumption = double.Parse(match.Groups[1].Value.Replace(",", ""));
        }
    }
}
