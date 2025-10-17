using System.Text.Json;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

public class RecordalAck(Receipt receipt, string url, Filling model, string applicationId) : IDocument
{
    string nairaSymbol = "\u20A6";
    private Filling model { get; set; } = model;
    private Receipt receipt { get; set; } = receipt;
    private string url { get; set; } = url;
    private string ApplicationId { get; set; } = applicationId;

public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(35);
            page.Content().Element(ComposeContent);
        });
    }

    static IContainer HeaderElement(IContainer container)
    {
        return container
            .Border(1)
            .ShowOnce()
            .MinHeight(20)
            .AlignMiddle()
            .Background(Colors.Grey.Lighten3)
            .PaddingVertical(1)
            .PaddingLeft(5);
    }

    static IContainer Block(IContainer container)
    {
        return container
            .Border(1)
            .ShowOnce()
            .MinHeight(20)
            .PaddingVertical(3)
            .PaddingLeft(5)
            .AlignLeft();
    }
    void ComposeContent(IContainer container)
    {
        // Query the ApplicationHistory
        var recordalData = model.ApplicationHistory
            .FirstOrDefault(x => x.ApplicationType == FormApplicationTypes.DataUpdate &&
                                 x.CurrentStatus == ApplicationStatuses.AutoApproved &&
                                 x.id == ApplicationId);

        var fieldToChange = recordalData?.FieldToChange ?? "field";
        var oldValue = recordalData?.OldValue ?? "N/A";
        var newValue = recordalData?.NewValue ?? "N/A";

        string formattedOldValue = ExtractAllProperties(oldValue);
        string formattedNewValue = ExtractAllProperties(newValue);

        container
            .PaddingVertical(5)
            .Column(column =>
            {
                // Header with coat of arms and ministry information
                column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA").LineHeight(2).FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().Height(10);
                column.Item().AlignCenter().Text("RECORDAL ACKNOWLEDGEMENT LETTER").FontColor(Colors.Green.Darken3).FontFamily(Fonts.TimesNewRoman).FontSize(16).ExtraBold();
                column.Item().Height(25);

                //Payment Section
                column.Item().Table(table =>
                {
                    var date = receipt.Date;
                    var amount = receipt.Amount != null ? Convert.ToInt64(receipt.Amount).ToString("N0") : "-";
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("RECORDAL PAYMENT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Payment Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(date).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Payment rrr:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(receipt.rrr).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("File Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(receipt.FileId).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Amount Paid:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text($"{nairaSymbol} {amount}").FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().ColumnSpan(2).Element(Block).Column(c => {
                        c.Item().Text("Fee Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(receipt.PaymentFor).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                });
                // Applicant Information Section
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("APPLICANT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Applicant Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Name).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Email).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Phone).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Nationality:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].country).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().ColumnSpan(2).Element(Block).Column(c => {
                        c.Item().Text("Applicant Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Address).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                });
                //Recordal Information
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("RECORDAL INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Recordal Request Type:").FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text(fieldToChange).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    // Display Old Value
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Old:").FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        if ((fieldToChange == "Attachments") || (fieldToChange == "TrademarkLogo"))
                        {
                            c.Item().Text("File Attachment").FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        }
                        else
                        {
                            c.Item().Text(formattedOldValue).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        }
                    });

                    // Display New Value
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("New:").FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        if ((fieldToChange == "Attachments") || (fieldToChange == "TrademarkLogo"))
                        {
                            c.Item().Text("File Attachment").FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        }
                        else
                        {
                            c.Item().Text(formattedNewValue).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        }
                    });
                });
                // Correspondence Information Section
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("CORRESPONDENCE INFORMATION (ADDRESS OF SERVICE)").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.Correspondence.name).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.Correspondence.address).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.Correspondence.email).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.Correspondence.phone).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                });
                column.Item().Height(40);

                // Notification Message 
                column.Item().AlignCenter().Text("THIS IS TO NOTIFY YOU THAT YOUR APPLICATION HAS BEEN")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                column.Item().AlignCenter().Text("RECEIVED AND IS RECEIVING DUE ATTENTION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
            });
    }

    private string ExtractAllProperties(string json)
    {
        try
        {
            // Check if the input is a JSON array
            if (json.StartsWith("[") && json.EndsWith("]"))
            {
                var jsonArray = JsonSerializer.Deserialize<JsonElement[]>(json);
                var formattedArray = jsonArray.Select(element =>
                {
                    if (element.ValueKind == JsonValueKind.Object)
                    {
                        var properties = element.EnumerateObject()
                            .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)) // Exclude "Id"
                            .Select(property => $"{property.Name}: {property.Value}")
                            .ToArray();
                        return string.Join("\n", properties);
                    }
                    return element.ToString();
                });
                return string.Join("\n\n", formattedArray); // Separate each object with a blank line
            }

            // Handle single JSON object
            var jsonElement = JsonSerializer.Deserialize<JsonElement>(json);
            var extractedProperties = jsonElement.EnumerateObject()
                .Where(property => !string.Equals(property.Name, "Id", StringComparison.OrdinalIgnoreCase)) // Exclude "Id"
                .Select(property => $"{property.Name}: {property.Value}")
                .ToArray();
            return string.Join("\n", extractedProperties);
        }
        catch
        {
            return json; // Return the original string if it's not valid JSON
        }
    }

}