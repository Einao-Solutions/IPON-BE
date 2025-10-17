using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign
{
    public class RejectionModelTrademark(Filling model, string url, byte[] signatureUrl, string examinerName, byte[] image) : IDocument
    {
        private Filling model { get; set; } = model;
        private string url { get; set; } = url;
        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(40);
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

        void ComposeContent(IContainer container)
        {
            container
                .PaddingVertical(1)
                .Column(column =>
                {
                    // Header with coat of arms and ministry information
                    column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                    column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA").LineHeight(2).FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().Height(10);
                    column.Item().AlignCenter().Text("TRADEMARK REFUSAL LETTER").FontColor(Colors.Green.Darken3).FontFamily(Fonts.TimesNewRoman).FontSize(16).ExtraBold();
                    column.Item().Height(20);

                    column.Item().Height(5);

                    
                    
                    // Filing Information Section
                    // column.Item().Table(table =>
                    // {
                    //     table.ColumnsDefinition(columns =>
                    //     {
                    //         columns.RelativeColumn();
                    //         columns.RelativeColumn();
                    //     });
                    //
                    //     table.Cell().ColumnSpan(2).Element(HeaderElement).Text("FILING INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    //
                    //     table.Cell().Element(Block).Column(c => {
                    //         c.Item().Text("File Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                    //         c.Item().Text(model.FileId).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    //     });
                    //
                    //     table.Cell().Element(Block).Column(c => {
                    //         c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                    //         c.Item().Text(model.applicants[0].Email).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    //     });
                    //     table.Cell().Element(Block).Column(c => {
                    //         c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                    //         c.Item().Text(model.applicants[0].Phone).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    //     });
                    //     table.Cell().Element(Block).Column(c => {
                    //         c.Item().Text("Nationality:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                    //         c.Item().Text(model.applicants[0].country).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    //     });
                    //     table.Cell().ColumnSpan(2).Element(Block).Column(c => {
                    //         c.Item().Text("Applicant Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                    //         c.Item().Text(model.applicants[0].Address).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                    //     });
                    // });
                    //
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
                            if (model.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device && model.Attachments.FirstOrDefault(e => e.name == "representation") != null && image.Length > 0)
                            {
                                var img = Image.FromBinaryData(image);
                                c.Item().Height(100).AlignCenter().Image(img).FitArea();
                            }
                            else
                                c.Item().Text(model.TrademarkLogo).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });


                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Product class:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.TrademarkClass).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });

                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("Claims/Disclaimer:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.TrademarkDisclaimer).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c => {
                            c.Item().Text("Description of Goods:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.TrademarkClassDescription).FontSize(12).FontFamily(Fonts.TimesNewRoman);
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
                            c.Item().Text("REFUSAL DATE:").FontSize(12).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text(model.ApplicationHistory.LastOrDefault()?.StatusHistory.LastOrDefault().Date.ToString("dd/MM/yyyy")).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });

                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text("REASON FOR REFUSAL:").FontSize(12).FontFamily(Fonts.TimesNewRoman).SemiBold();
                        });
                        table.Cell().Element(Block).Column(c => {
                            c.Item().Text(model.ApplicationHistory.LastOrDefault()?.StatusHistory.LastOrDefault(s => s.afterStatus == ApplicationStatuses.Rejected)?.Message).FontSize(12).FontFamily(Fonts.TimesNewRoman);
                        });
                    });

                    column.Item().Height(40);

                    // Notification Message 
                    column.Item().AlignCenter().Text("THIS IS TO NOTIFY YOU THAT YOUR APPLICATION HAS BEEN")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold().FontColor(Colors.Green.Darken3);
                    column.Item().AlignCenter().Text("OPPOSED BY THE EXAMINING OFFICER. YOU HAVE 30 DAYS TO")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold().FontColor(Colors.Green.Darken3);
                    column.Item().AlignCenter().Text("FILE AN APPEAL TO THE REGISTRAR OF TRADEMARKS.")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold().FontColor(Colors.Green.Darken3);

                    //// QR Code at the bottom
                    //column.Item().Height(50);
                    //column.Item().AlignCenter().Height(100).Element(GetQrCode);

                    //column.Spacing(5);
                });
        }
        //
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

