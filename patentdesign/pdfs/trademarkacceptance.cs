using Microsoft.IdentityModel.Tokens;
using patentdesign.Models;
using PDFtoImage;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using PDFtoImage;

namespace patentdesign
{
    public class AcceptanceModelTrademark(Filling model, string url, byte[] signatureUrl, string examinerName, byte[]image) : IDocument
    {
        private Filling model { get; set; } = model;
        private string url { get; set; } = url;
        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
               
            });
        }
        static IContainer SNBlock(IContainer container)
        {
            return container
                .Border(1)
                .ShowOnce()
                .MaxWidth(30)
                .MinHeight(30)
                .AlignCenter()
                .AlignMiddle();
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
        static IContainer AttachmentStyle(IContainer container)
        {
            return container
                .ShowOnce()
                .MaxWidth(80)
                .AlignCenter()
                .AlignMiddle();
        }
        void ComposeContent(IContainer container)
        {
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
                    column.Item().AlignCenter().Text("TRADEMARK ACCEPTANCE LETTER").FontColor(Colors.Green.Darken3).FontFamily(Fonts.TimesNewRoman).FontSize(16).ExtraBold();
                    column.Item().Height(25);
                    var date = model.FilingDate ?? model.DateCreated;
                    var exDate = model?.ApplicationHistory?[0].StatusHistory
                        .FirstOrDefault(a => a.afterStatus == ApplicationStatuses.Publication);
                    bool isRejected = model?.ApplicationHistory?[0].StatusHistory?.Any(s => s.afterStatus == ApplicationStatuses.Rejected) ?? false;
                    var appeal = model?.ApplicationHistory?.FirstOrDefault(d =>
                        d.ApplicationType == FormApplicationTypes.AppealRequest);
                    var appealDate = appeal?.StatusHistory.FirstOrDefault(f=> f.afterStatus == ApplicationStatuses.Approved);
                    var payDay = model?.ApplicationHistory?
                        .FirstOrDefault(s => s.CurrentStatus == ApplicationStatuses.Active)?.ApplicationDate;
                    var rrr = model?.ApplicationHistory?[0].PaymentId;
                    //File Information Section
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });
                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("FILE INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Filing Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(date.ToString("dd MMMM, yyyy")).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("File Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.FileId).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Payment Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(date.ToString("dd MMMM, yyyy")).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Payment ID:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(rrr).FontSize(12).FontFamily(Fonts.TimesNewRoman);
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

                    // Trademark Information Section
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("TRADEMARK INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Product Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.TitleOfTradeMark).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Representation of Trademark:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            if ((model.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device) &&
                                model.Attachments.FirstOrDefault(e => e.name == "representation") != null &&
                                image != null && image.Length > 0)
                            {
                                try
                                {
                                    byte[] imageBytes = image;
                                    var representation = model.Attachments.First(e => e.name == "representation");

                                    // Convert PDF to image if needed
                                    if (representation.url[0].EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                                    {
                                        using var pdfStream = new MemoryStream(image);
                                        using var imageStream = new MemoryStream();
                                        PDFtoImage.Conversion.SavePng(imageStream, pdfStream, page: 0, options: new RenderOptions { Dpi = 150 });
                                        imageBytes = imageStream.ToArray();
                                    }

                                    c.Item().Height(100).AlignCenter().Image(imageBytes).FitArea();
                                }
                                catch
                                {
                                    c.Item().Text("Unable to display representation").FontSize(12).FontColor(Colors.Red.Medium);
                                }
                            }
                            else
                            {
                                c.Item().Text(model.TrademarkLogo.GetDisplayName()).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                            }
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Product class:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.TrademarkClass).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });

                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Claims/Disclaimer:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            if (!model.TrademarkDisclaimer.IsNullOrEmpty())
                                c.Item().Text(model.TrademarkDisclaimer).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                            else
                            {
                                c.Item().Text("N/A").FontSize(12).FontFamily(Fonts.TimesNewRoman);
                            }
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c => {
                            c.Item().Text("Trademark Specification:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model?.TrademarkSpecification ?? model.TrademarkClassDescription).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                    });

                    // Process Information Section
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("PROCESS INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("EXAMINATION DATE:").FontSize(12).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        });
                        table.Cell().Element(Block).Column(c => {
                            var dateStr = isRejected 
                                ? (appealDate?.Date != default ? appealDate.Date.ToString("dd/MM/yyyy") : "N/A")
                                : (exDate?.Date != default ? exDate.Date.ToString("dd/MM/yyyy") : "N/A");
                            c.Item().Text(dateStr).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });


                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("EXAMINING OFFICER:").FontSize(12).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text(examinerName).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                    });
                    column.Item().Height(40);
                    // Notification Message 
                    column.Item().AlignCenter().Text("THIS IS TO NOTIFY YOU THAT YOUR APPLICATION HAS BEEN")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                    column.Item().AlignCenter().Text("ACCEPTED AND WILL IN DUE COURSE BE ADVERTISED")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                    column.Item().AlignCenter().Text("IN THE TRADEMARKS JOURNAL")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                    //QR Code

                    column.Item().Height(100).AlignCenter().Element(GetQrCode);


                });
        }
        private void GetQrCode(IContainer container)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeImage = qrCode.GetGraphic(20);
                container.Image(qrCodeImage).FitArea();
            }
        }
    }
}