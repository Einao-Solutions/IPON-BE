using System.Globalization;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class mergerAck(Filling model, byte[] image, string applicationId) : IDocument
    {
        //string nairaSymbol = "\u20A6";
        private Filling model { get; set; } = model;
        //private string url { get; set; } = url;

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
            });
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
            var app = model.PostRegApplications?.FirstOrDefault(a => a.Id == applicationId);
            
            // Check if model and applicants are valid
            if (model?.applicants == null || model.ApplicationHistory == null || app == null)
            {
                container.PaddingVertical(5)
                    .Column(column =>
                    {
                        column.Item().AlignCenter().Text("Invalid applicant data").FontSize(16)
                            .FontColor(Colors.Red.Medium);
                    });
                return;
            }

            container
                .PaddingVertical(5)
                .Column(column =>
                {
                    // Header with coat of arms and ministry information
                    column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                    column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA").LineHeight(2)
                        .FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman)
                        .FontSize(14).Bold();
                    column.Item().Height(10);
                    column.Item().AlignCenter().Text("MERGER ACKNOWLEDGEMENT LETTER").FontColor(Colors.Green.Darken3)
                        .FontFamily(Fonts.TimesNewRoman).FontSize(16).ExtraBold();
                    column.Item().Height(25);



                    //Payment Information Section
                    column.Item().Table(table =>
                    {
                        var filingDate = DateTime.TryParseExact(
                            app.FilingDate,
                            "M/d/yyyy h:mm:ss tt",
                            CultureInfo.InvariantCulture,
                            DateTimeStyles.None,
                            out var parsedDate)
                            ? parsedDate.ToString("dd MMMM yyyy")
                            : "Invalid date";
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("PAYMENT INFORMATION")
                            .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Filing Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(filingDate).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });

                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Payment rrr:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(app.rrr).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("File Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(app.FileNumber).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Fee Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text("Application for Recordal of Merger").FontSize(12)
                                .FontFamily(Fonts.TimesNewRoman);
                        });
                    });
                    // Applicant Information Section
                    column.Item().Table(table =>
                    {
                        var mergerDate = DateTime.TryParse(app.dateOfRecordal, out var parsedMergerDate)
                            ? parsedMergerDate.ToString("dd MMMM, yyyy")
                            : "Invalid date";
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });
                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("MERGER INFORMATION")
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
                            c.Item().Text("Date of Merger:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(mergerDate).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
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

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("TRADEMARK INFORMATION")
                            .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Product Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.TitleOfTradeMark).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Representation of Trademark:").FontSize(10).FontFamily(Fonts.TimesNewRoman)
                                .SemiBold();
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
                            c.Item().Text("Product class:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.TrademarkClass).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });

                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Claims/Disclaimer:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            if (!string.IsNullOrEmpty(model.TrademarkDisclaimer))
                                c.Item().Text(model.TrademarkDisclaimer).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                            else
                            {
                                c.Item().Text("N/A").FontSize(12).FontFamily(Fonts.TimesNewRoman);
                            }
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Description of Goods:").FontSize(10).FontFamily(Fonts.TimesNewRoman)
                                .SemiBold();
                            c.Item().Text(model.TrademarkClassDescription).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                    });


                    column.Item().Height(40);

                    // Notification Message 
                    column.Item().AlignCenter().Text("YOUR APPLICATION HAS BEEN")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                    column.Item().AlignCenter().Text("RECEIVED AND IS RECEIVING DUE ATTENTION")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(12).Bold().FontColor(Colors.Green.Darken3);
                });
        }
        //private void GetQrCode(IContainer container)
        //{
        //    using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
        //    using (QRCodeData qrCodeData = qrGenerator.CreateQrCode(url, QRCodeGenerator.ECCLevel.Q))
        //    using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
        //    {
        //        byte[] qrCodeImage = qrCode.GetGraphic(20);
        //        container.Image(qrCodeImage).FitArea();
        //    }
        //}
    }
}