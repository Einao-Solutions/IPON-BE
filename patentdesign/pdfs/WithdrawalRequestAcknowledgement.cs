using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class WithdrawalRequestAcknowledgement(Filling model, byte[] image, ApplicationInfo selectedHistory) : IDocument
    {
        private Filling model { get; set; } = model;
        private ApplicationInfo selectedHistory { get; set; } = selectedHistory;

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
            var applicant = (model?.applicants != null && model.applicants.Count > 0) ? model.applicants[0] : null;

            container
                .PaddingVertical(5)
                .Column(column =>
                {
                    // Header
                    column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                    column.Item().AlignCenter().Text("FEDERAL REPUBLIC OF NIGERIA").LineHeight(2).FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                    column.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().AlignCenter().Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    column.Item().Height(10);

                    // Dynamic Report Title
                    string reportTitle = model.Type switch
                    {
                        FileTypes.TradeMark => "TRADEMARK WITHDRAWAL ACKNOWLEDGEMENT LETTER",
                        FileTypes.Patent => "PATENT WITHDRAWAL ACKNOWLEDGEMENT LETTER",
                        FileTypes.Design => "DESIGN WITHDRAWAL ACKNOWLEDGEMENT LETTER",
                        _ => "WITHDRAWAL ACKNOWLEDGEMENT LETTER"
                    };
                    column.Item().AlignCenter().Text(reportTitle).FontColor(Colors.Green.Darken3).FontFamily(Fonts.TimesNewRoman).FontSize(16).ExtraBold();
                    column.Item().Height(25);

                    // Filing Information Section
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("FILING INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Filing date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.FilingDate?.ToString("dd MMM yyyy") ?? "N/A").FontSize(12).FontColor(model.FilingDate == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Payment rrr:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(selectedHistory?.PaymentId ?? "N/A").FontSize(12).FontColor(selectedHistory?.PaymentId == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("File number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.FileId ?? "N/A").FontSize(12).FontColor(string.IsNullOrWhiteSpace(model.FileId) ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Withdrawal Request Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.WithdrawalRequestDate?.ToString("dd MMM yyyy") ?? "N/A").FontSize(12).FontColor(model.WithdrawalRequestDate == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
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
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(applicant?.Name ?? "N/A").FontSize(12).FontColor(applicant?.Name == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(applicant?.Email ?? "N/A").FontSize(12).FontColor(applicant?.Email == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Phone number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(applicant?.Phone ?? "N/A").FontSize(12).FontColor(applicant?.Phone == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Nationality:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(applicant?.country ?? "N/A").FontSize(12).FontColor(applicant?.country == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(applicant?.Address ?? "N/A").FontSize(12).FontColor(applicant?.Address == null ? Colors.Black  : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
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

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("CORRESPONDENCE INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.name ?? "N/A").FontSize(12).FontColor(model?.Correspondence?.name == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.email ?? "N/A").FontSize(12).FontColor(model?.Correspondence?.email == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Phone number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.phone ?? "N/A").FontSize(12).FontColor(model?.Correspondence?.phone == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Nationality:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.state ?? "N/A").FontSize(12).FontColor(model?.Correspondence?.state == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.address ?? "N/A").FontSize(12).FontColor(model?.Correspondence?.address == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                    });

                    // Unique Section per File Type
                    switch (model.Type)
                    {
                        case FileTypes.TradeMark:
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                });

                                table.Cell().ColumnSpan(2).Element(HeaderElement).Text("TRADEMARK INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TitleOfTradeMark ?? "N/A").FontSize(12).FontColor(model.TitleOfTradeMark == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
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
                                    c.Item().Text("Description of goods:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TrademarkClassDescription ?? "N/A").FontSize(12).FontColor(model.TrademarkClassDescription == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Product Class:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TrademarkClass?.ToString() ?? "N/A").FontSize(12).FontColor(model.TrademarkClass == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                                });
                                table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                                {
                                    c.Item().Text("Claims/Disclaimer:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TrademarkDisclaimer ?? "N/A").FontSize(12).FontColor(model.TrademarkDisclaimer == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                                });
                            });
                            break;

                        case FileTypes.Patent:
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                });

                                table.Cell().ColumnSpan(2).Element(HeaderElement).Text("PATENT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Title Of Invention:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TitleOfInvention ?? "N/A").FontSize(12).FontColor(model.TitleOfInvention == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("File Origin:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.FileOrigin ?? "N/A").FontSize(12).FontColor(model.FileOrigin == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Patent Type:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.PatentType?.ToString() ?? "N/A").FontSize(12).FontColor(model.PatentType == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Application Type:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.PatentApplicationType?.ToString() ?? "N/A").FontSize(12).FontColor(model.PatentApplicationType == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                                });
                            });
                            break;

                        case FileTypes.Design:
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(columns =>
                                {
                                    columns.RelativeColumn();
                                    columns.RelativeColumn();
                                });

                                table.Cell().ColumnSpan(2).Element(HeaderElement).Text("DESIGN INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Title Of Design:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TitleOfDesign ?? "N/A").FontSize(12).FontColor(model.TitleOfDesign == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Design Type:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.DesignType?.ToString() ?? "N/A").FontSize(12).FontColor(model.DesignType == null ? Colors.Black : Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                                });
                            });
                            break;
                    }

                    column.Item().Height(10);
                    column.Item().AlignCenter().Text("YOUR APPLICATION IS BEING PROCESSED").FontColor(Colors.Green.Darken2).FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                });
        }
    }
}
