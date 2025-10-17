// your renewal applicaiton for 24/25 has been accepted for
// file id
// file number
// title of application
// date
// examiner name

using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs;

public class TrademarkRenewalCertificate(Filling model, string url,string applicationId, byte[]? image, DateTime date) : IDocument
{
    private Filling Model { get; set; } = model;
    private string ApplicationId { get; set; } = applicationId;
    private string url { get; set; } = url;
    private DateTime Date { get; set; } = date;
    public void Compose(IDocumentContainer container)
    {
        container.Page(page =>
        {
            page.Margin(35);
            page.Content().Element(ComposeContent);
            page.Footer().Element(ComposeFooter);
            
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

    private void ComposeContent(IContainer container)
    {
        container
            .PaddingVertical(5)
            .Column(column =>
            {
                column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                column.Item().AlignCenter().Text("NIGERIA");
                //column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT");
                //column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT");
                //column.Item().AlignCenter().Text("PATENTS AND DESIGNS ACT CAP 344, LFN 1990");
                column.Item().Height(10);
                column.Item().AlignCenter().Text("Certificate of Renewal").FontFamily("Certificate").FontSize(30)
                    .Bold().FontColor(Colors.Green.Darken3);
                column.Item().Height(10);
                column.Item().AlignCenter().Text($"TRADE MARKS ACT").FontFamily(Fonts.TimesNewRoman)
                    .FontSize(14).Bold();
                column.Item().Height(5);
                column.Item().AlignCenter()
                    .Text($"(CAP 436 Laws Of The Federation of Nigeria 1990; Section 22 (3) Regulation 65)")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                column.Item().Height(25);
                //Table
                // Applicant Information Section
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("Applicant Information")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Name).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Email).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Phone).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.applicants[0].Address).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                });
                //Renewal Information
                column.Item().Table(table =>
                {
                    // Console.WriteLine("Date: "+Date);
                    var dueDate = (model.ApplicationHistory[0].ApplicationDate).AddYears(7);
                    DateTime nextRenewalDate = dueDate.AddYears(14);
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });
                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("Renewal Information").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    //table.Cell().Element(Block).Column(c =>
                    //{
                    //    c.Item().Text("Renewal Type:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                    //    c.Item().Text(renewalType).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    //});
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Renewal Due Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(dueDate.ToString("dd MMMM, yyyy")).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Next Renewal Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(nextRenewalDate.ToString("dd MMMM, yyyy")).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                });
                // Trademark Information Section
                column.Item().Table(table =>
                {
                    table.ColumnsDefinition(columns =>
                    {
                        columns.RelativeColumn();
                        columns.RelativeColumn();
                    });

                    table.Cell().ColumnSpan(2).Element(HeaderElement).Text("Trademark Information")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("File Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.FileId).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("RTM:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.RtmNumber).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Product Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.TitleOfTradeMark).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Class of goods:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.TrademarkClass).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                    table.Cell().Element(Block).Column(c => {
                        c.Item().Text("Representation of Trademark:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        if (model.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device &&
                            model.Attachments.FirstOrDefault(e => e.name == "representation") != null &&
                            image != null && image.Length > 0)
                        {
                            try
                            {
                                var img = Image.FromBinaryData(image);
                                c.Item().Height(100).AlignCenter().Image(img).FitArea();
                            }
                            catch
                            {
                                c.Item().Text("Invalid image data").FontSize(12).FontColor(Colors.Red.Medium);
                            }
                        }
                        else
                        {
                            c.Item().Text(model.TrademarkLogo).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        }
                    });
                    table.Cell().Element(Block).Column(c =>
                    {
                        c.Item().Text("Description:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        c.Item().Text(model.TrademarkClassDescription).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    });
                });
                column.Item().Height(30);
                // Notification Message 
                // column.Item().AlignCenter().Text("THIS IS TO NOTIFY YOU THAT YOUR RENEWAL REQUEST HAS BEEN PROCESSED")
                    // .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                column.Item().Text($"Sealed at my direction, \n{date}").SemiBold().FontFamily(Fonts.TimesNewRoman);
                column.Item().Height(35).Image("assets/reg.png").FitArea();
                column.Item().Height(20);
                column.Item().Text("Abubakar Abdullahi").FontFamily(Fonts.TimesNewRoman);
                column.Item().Text("For Registrar,").SemiBold().FontFamily(Fonts.TimesNewRoman);
                column.Item().Text("Trade Marks Registry,").SemiBold().FontFamily(Fonts.TimesNewRoman);
                column.Item().Text("Federal Ministry of Industry, Trade and Investment.").SemiBold()
                    .FontFamily(Fonts.TimesNewRoman);
                column.Item().Text("Federal Capital Territory").SemiBold().FontFamily(Fonts.TimesNewRoman);
                column.Item().Height(40);
                
            });
            
        
    }
    private void ComposeFooter(IContainer container)
    {
        container.Column(c =>
        {
            c.Item().BorderTop(2).BorderColor(Colors.Green.Darken3);
            c.Item().Height(10);
            c.Item().AlignBottom().Row(row =>
            {
                
                row.RelativeItem().AlignLeft().Column(c => { c.Item().AlignCenter().Element(GetQrCode); });
                row.RelativeItem().AlignRight().Column(c =>
                {
                    c.Item().Height(80).Image("assets/Commeciallawdepartmentlogo.png").FitArea();
                });
            });
            c.Item().Text("Scan the QR code to verify the document.").Italic().AlignCenter().FontSize(8);
            IContainer BlockStyle(IContainer container) =>
                container.Background(Colors.Green.Darken3).Padding(10);
        });
    }
               

    private void GetQrCode(IContainer container)
    {
        using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
        using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
        using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
        {
            byte[] qrCodeImage = qrCode.GetGraphic(10);
            container.Height(80).Width(80).Image(qrCodeImage).FitArea();
        }
    }
}