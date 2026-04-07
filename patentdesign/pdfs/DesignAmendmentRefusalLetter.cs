using System;
using System.Linq;
using patentdesign.Enums;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class DesignAmendmentRefusalLetter : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly Receipt receipt;
        private readonly ApplicationInfo application;
        private readonly string appId;

        public DesignAmendmentRefusalLetter(Filling model, string url, Receipt receipt, ApplicationInfo application, string appId)
        {
            this.model = model;
            this.url = url;
            this.receipt = receipt;
            this.application = application;
            this.appId = appId;
        }

        public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
            });
        }

        private static IContainer Box(IContainer c) => c
            .Border(1)
            .Padding(5)
            .AlignLeft();

        private static IContainer Header(IContainer c) => c
            .Border(1)
            .Background(Colors.Grey.Lighten1);

        private static void WriteText(IContainer cell, string text)
        {
            var placeholder = text == "N/A";
            cell.Text(text)
                .FontFamily(Fonts.TimesNewRoman)
                .FontSize(12)
                .Italic(placeholder)
                .FontColor(Colors.Black);
        }

        private static string F(object? v) => v switch
        {
            null => "N/A",
            string s when string.IsNullOrWhiteSpace(s) => "N/A",
            DateTime dt when dt == default => "N/A",
            DateTime dt => dt.ToString("dd MMMM, yyyy"),
            _ => v.ToString() ?? "N/A"
        };

        private void ComposeContent(IContainer container)
        {
            container.Column(col =>
            {
                var date = "-";
                if (!string.IsNullOrWhiteSpace(receipt.Date) && DateTime.TryParse(receipt.Date, out var parsedDate))
                {
                    date = parsedDate.ToString("dd/MM/yyyy");
                }

                // Header
                col.Item().Height(60).AlignCenter().PaddingBottom(10).Image("assets/logo.png").FitArea();
                col.Item().AlignCenter().PaddingBottom(10).Text("FEDERAL REPUBLIC OF NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                col.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().PaddingBottom(10).Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().Text("DESIGN AMENDMENT REFUSAL LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(16).FontColor(Colors.Red.Darken2).ExtraBold();
                col.Item().Height(10);

                // PAYMENT INFORMATION
                TwoColumnSection(col, "PAYMENT INFORMATION", new[]
                {
                    ("Filing date:", F(date)),
                    ("Payment rrr:", F(receipt.rrr)),
                    ("File number:", F(model.FileId)),
                    ("Fee title:", F(receipt.PaymentFor)),
                });

                // AMENDMENT DATA
                var amendmentRecordal = model.PostRegApplications?
                    .FirstOrDefault(p => p.RecordalType == "Design Amendment" && p.Id == appId);

                if (amendmentRecordal != null)
                {
                    DisplayAmendmentInformation(col, amendmentRecordal);
                }
                else
                {
                    TwoColumnSection(col, "AMENDMENT INFORMATION", new[]
                    {
                        ("Amendment Type:", "N/A"),
                        ("Status:", "Amendment application was refused"),
                    });
                }

                // DESIGN INFORMATION
                col.Item().Element(Header).Text("DESIGN INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                FullWidthBox(col, "Title of Industrial Design:", F(model.TitleOfDesign));
                FullWidthBox(col, "Design Type:", F(model.DesignType));
                FullWidthBox(col, "Statement of Novelty:", F(model.StatementOfNovelty));

                // REFUSAL INFORMATION
                col.Item().Element(Header).Text("REFUSAL INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                var refusalHistory = application.StatusHistory.LastOrDefault(h => h.afterStatus == ApplicationStatuses.Rejected);

                var officerName = refusalHistory?.User ?? "-";
                var reason = refusalHistory?.Message ?? "-";

                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Officer's Name:", officerName),
                    ("Reason:", reason)
                });

                col.Item().AlignCenter().PaddingTop(30).Text("YOUR APPLICATION HAS BEEN REFUSED")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Red.Darken2);
            });
        }

        private void DisplayAmendmentInformation(ColumnDescriptor col, PostRegistrationApp amendment)
        {
            col.Item().Element(Header).Text("AMENDMENT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
            FullWidthBox(col, "Amendment Type:", GetAmendmentTypeDisplay(amendment.AmendmentType));
            FullWidthBox(col, "Description:", amendment.message ?? "Amendment requested");
        }

        private string GetAmendmentTypeDisplay(string amendmentType)
        {
            return amendmentType switch
            {
                "ApplicantName" => "Applicant Name Amendment",
                "ApplicantAddress" => "Applicant Address Amendment",
                "DesignTitle" => "Design Title & Details Amendment",
                "CorrespondenceInformation" => "Correspondence Information Amendment",
                "PriorityInfo" => "Priority Information Amendment",
                "AddAndRemoveApplicant" => "Add/Remove Applicants Amendment",
                "DesignType" => "Design Type Amendment",
                "StatementOfNovelty" => "Statement of Novelty Amendment",
                _ => amendmentType ?? "Unknown Amendment Type"
            };
        }

        private static void TwoColumnSection(ColumnDescriptor col, string title, (string Label, string Value)[] pairs)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                col.Item().Element(Header).Text(title).FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
            }

            for (var i = 0; i < pairs.Length; i += 2)
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(Box).Column(c2 =>
                    {
                        c2.Item().Text(pairs[i].Label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                        WriteText(c2.Item(), pairs[i].Value);
                    });

                    if (i + 1 < pairs.Length)
                    {
                        row.RelativeItem().Element(Box).Column(c2 =>
                        {
                            c2.Item().Text(pairs[i + 1].Label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                            WriteText(c2.Item(), pairs[i + 1].Value);
                        });
                    }
                    else
                    {
                        row.RelativeItem();
                    }
                });
            }
        }

        private static void FullWidthBox(ColumnDescriptor col, string label, string value)
        {
            col.Item().Element(Box).Column(c2 =>
            {
                if (!string.IsNullOrEmpty(label))
                {
                    c2.Item().Text(label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                }

                WriteText(c2.Item(), value);
            });
        }
    }
}
