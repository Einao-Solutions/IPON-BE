// using QRCoder;

using System.Globalization;
using Microsoft.IdentityModel.Tokens;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using static System.Net.Mime.MediaTypeNames;
using Image = QuestPDF.Infrastructure.Image;

namespace patentdesign.pdfs
{
    public class RenewalAck(Filling model, byte[] image, string applicationId, DateTime paydate) : IDocument
    {
        string nairaSymbol = "\u20A6";
        private Filling model { get; set; } = model;
        private string applicationId { get; set; } = applicationId;
        private DateTime paydate { get; set; } = paydate;
        
        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
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
            // Check if model and applicants are valid
            if (model?.applicants == null || model.applicants.Count == 0)
            {
                container.PaddingVertical(5)
                    .Column(column =>
                    {
                        column.Item().AlignCenter().Text("Invalid applicant data").FontSize(16).FontColor(Colors.Red.Medium);
                    });
                return;
            }
            // Determine renewal type 
            var renewalHistory = model.ApplicationHistory
                .Where(x => x.ApplicationType == FormApplicationTypes.LicenseRenewal)
                .OrderBy(x => x.ApplicationDate)
                .ToList();

            string renewalType = renewalHistory.Count switch
            {
                1 => "1st Renewal",
                2 => "2nd Renewal",
                3 => "3rd Renewal",
                _ => $"{renewalHistory.Count}th Renewal"
            };
            var date = (model.FilingDate ?? model.ApplicationHistory[0].ApplicationDate);
            var dueDate = date.AddYears(7);
            DateTime nextRenewalDate = dueDate.AddYears(14);
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
                    column.Item().AlignCenter().Text("RENEWAL ACKNOWLEDGEMENT LETTER").FontColor(Colors.Green.Darken3).FontFamily(Fonts.TimesNewRoman).FontSize(16).ExtraBold();
                    column.Item().Height(25);

                    //Payment Section
                    column.Item().Table(table =>
                    {
                        var app = model.ApplicationHistory.FirstOrDefault(a => a.id == applicationId);
                        
                        var date = app.ApplicationDate.ToString("dd MMMM, yyyy")?? "N/A";
                        // var amount = long.TryParse(receipt.Amount, out var parsedAmount) ? parsedAmount.ToString("N0") : "0";
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });
                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("RENEWAL PAYMENT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Payment Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(date).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Payment rrr:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(app.PaymentId).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("File Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.FileId).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        //table.Cell().Element(Block).Column(c => {
                        //    c.Item().Text("Amount Paid:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        //    c.Item().Text($"{nairaSymbol} {amount}").FontFamily(Fonts.TimesNewRoman);
                        //});
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Fee Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text("Renewal of Registered Trademark").FontSize(12).FontFamily(Fonts.TimesNewRoman);
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
                            c.Item().Text("Description of Goods:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.TrademarkClassDescription).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                    });

                    //Renewal Information
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });
                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("RENEWAL INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
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
        
        // private void GetQrCode(IContainer container)
        // {
        //     using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
        //     using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
        //     using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
        //     {
        //         byte[] qrCodeImage = qrCode.GetGraphic(20);
        //         container.Image(qrCodeImage).FitArea();
        //     }
        // }
    }
}