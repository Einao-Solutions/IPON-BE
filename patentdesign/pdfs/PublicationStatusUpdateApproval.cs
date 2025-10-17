using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class PublicationStatusUpdateApproval(Filling model, byte[] image, ApplicationInfo selectedHistory) : IDocument
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
            if (model?.applicants == null || model.applicants.Count == 0)
            {
                container.PaddingVertical(5)
                    .Column(column =>
                    {
                        column.Item().AlignCenter().Text("Invalid applicant data").FontSize(16).FontColor(Colors.Black);
                    });
                return;
            }

            var applicant = model.applicants[0];

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
                        FileTypes.TradeMark => "PUBLICATION STATUS UPDATE APPROVAL LETTER",
                        FileTypes.Patent => "PATENT STATUS REPORT",
                        FileTypes.Design => "DESIGN STATUS REPORT",
                        _ => "STATUS REPORT"
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
                            c.Item().Text("Filing Date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.FilingDate).FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Publication date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.PublicationDate).FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("File Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.FileId ?? "").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                        });

                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Status Update Request date:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(model.PublicationRequestDate).FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                        });

                        var paymentId = selectedHistory?.PaymentId ?? "N/A";
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Payment rrr:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(paymentId).FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
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
                            c.Item().Text(applicant.Name ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(applicant.Email ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(applicant.Phone ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Nationality:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(applicant.country ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                            c.Item().Text(applicant.Address ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                        });
                    });

                    // CORRESPONDENCE INFORMATION
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
                            c.Item().Text(model?.Correspondence?.name ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Email:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.email ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Phone Number:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.phone ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("State:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.state ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                        {
                            c.Item().Text("Address:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.Correspondence?.address ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
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
                                    c.Item().Text(model.TitleOfTradeMark ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Representation Of Trademark:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    if (model.TrademarkLogo is TradeMarkLogo.WordandDevice or TradeMarkLogo.Device && model.Attachments.FirstOrDefault(e => e.name == "representation") != null && image.Length > 0)
                                    {
                                        var img = Image.FromBinaryData(image);
                                        c.Item().Height(100).AlignCenter().Image(img).FitArea();
                                    }
                                    else
                                    {
                                        c.Item().Text(model.TrademarkLogo?.ToString() ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                                    }
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Description of Goods:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TrademarkClassDescription ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Product Class:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TrademarkClass?.ToString() ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                                });
                                table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                                {
                                    c.Item().Text("Claim/Disclaimer:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TrademarkDisclaimer ?? "N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
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
                                    c.Item().Text("Title of Invention:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TitleOfInvention ?? "").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Patent Type:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.PatentType?.ToString() ?? "").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                                });
                                table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                                {
                                    c.Item().Text("Application Type:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.PatentApplicationType?.ToString() ?? "").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                                });
                                table.Cell().ColumnSpan(2).Element(Block).Column(c =>
                                {
                                    c.Item().Text("Abstract:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.PatentAbstract ?? "").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
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
                                    c.Item().Text("Title:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.TitleOfDesign ?? "").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Design Type:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    c.Item().Text(model.DesignType?.ToString() ?? "").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                                });
                                table.Cell().Element(Block).Column(c =>
                                {
                                    c.Item().Text("Representation:").FontSize(10).FontFamily(Fonts.TimesNewRoman).SemiBold();
                                    // Show the image if available, otherwise show "N/A"
                                    if (image != null && image.Length > 0)
                                    {
                                        var img = Image.FromBinaryData(image);
                                        c.Item().Height(100).AlignCenter().Image(img).FitArea();
                                    }
                                    else
                                    {
                                        c.Item().Text("N/A").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                                    }
                                });
                            });
                            break;
                    }

                    // Application History Section
                    //column.Item().Table(table =>
                    //{
                    //    table.ColumnsDefinition(columns =>
                    //    {
                    //        columns.RelativeColumn();
                    //        columns.RelativeColumn();
                    //        columns.RelativeColumn();
                    //    });

                    //    table.Cell().ColumnSpan(3).Element(HeaderElement).Text("APPLICATION HISTORY").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                    //    table.Cell().Element(Block).Text("Date").FontFamily(Fonts.TimesNewRoman).SemiBold();
                    //    table.Cell().Element(Block).Text("Application Type").FontFamily(Fonts.TimesNewRoman).SemiBold();
                    //    table.Cell().Element(Block).Text("Status").FontFamily(Fonts.TimesNewRoman).SemiBold();

                    //    if (model.ApplicationHistory != null && model.ApplicationHistory.Count > 0)
                    //    {
                    //        foreach (var app in model.ApplicationHistory)
                    //        {
                    //            table.Cell().Element(Block).Text(app.ApplicationDate.ToString("dd MMMM, yyyy")).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                    //            table.Cell().Element(Block).Text(app.ApplicationType.ToString() ?? "").FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                    //            table.Cell().Element(Block).Text(app.CurrentStatus.ToString() ?? "").FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                    //        }
                    //    }
                    //    else
                    //    {
                    //        // At least one empty row
                    //        table.Cell().Element(Block).Text("").FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                    //        table.Cell().Element(Block).Text("").FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                    //        table.Cell().Element(Block).Text("").FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                    //    }
                    //});

                    //// Attachments Section
                    //column.Item().Table(table =>
                    //{
                    //    table.ColumnsDefinition(columns =>
                    //    {
                    //        columns.RelativeColumn();
                    //    });

                    //    table.Cell().Element(HeaderElement).Text("ATTACHMENTS").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                    //    if (model.Attachments != null && model.Attachments.Count > 0)
                    //    {
                    //        foreach (var att in model.Attachments)
                    //        {
                    //            table.Cell().Element(Block).Text(att.name ?? "").FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                    //        }
                    //    }
                    //    else
                    //    {
                    //        table.Cell().Element(Block).Text("No attachments").FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman);
                    //    }
                    //})
                    //;

                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(columns =>
                        {
                            columns.RelativeColumn();
                            columns.RelativeColumn();
                        });

                        table.Cell().ColumnSpan(2).Element(HeaderElement).Text("APPROVAL INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("officers Name:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model.ApplicationHistory.LastOrDefault()?.StatusHistory.LastOrDefault(s => s.afterStatus == ApplicationStatuses.Approved)?.User ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                        table.Cell().Element(Block).Column(c =>
                        {
                            c.Item().Text("Comment:").FontSize(10).FontFamily(Fonts.TimesNewRoman).Bold();
                            c.Item().Text(model?.PublicationReason ?? "Populate here").FontSize(12).FontColor(Colors.Black).FontFamily(Fonts.TimesNewRoman).Italic();
                        });
                    });

                    column.Item().Height(10);
                    column.Item().AlignCenter().Text("YOUR APPLICATION IS APPROVED").FontColor(Colors.Green.Darken2).FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                });
        }
    }
}

