using System;
using System.Linq;
using patentdesign.Enums;
using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class TrademarkCtcRefusalLetter : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly Receipt receipt;
        private readonly ApplicationInfo application;
        private readonly string appId;

        public TrademarkCtcRefusalLetter(Filling model, string url, Receipt receipt, ApplicationInfo application, string appId)
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
                if (!string.IsNullOrWhiteSpace(receipt.Date) &&
                    DateTime.TryParse(receipt.Date, out var parsedDate))
                {
                    date = parsedDate.ToString("dd/MM/yyyy");
                }

                col.Item().Height(60).AlignCenter().PaddingBottom(10)
                    .Image("assets/logo.png").FitArea();
                col.Item().AlignCenter().PaddingBottom(10)
                    .Text("FEDERAL REPUBLIC OF NIGERIA")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                col.Item().AlignCenter()
                    .Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().PaddingBottom(10)
                    .Text("COMMERCIAL LAW DEPARTMENT")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter()
                    .Text("TRADEMARK CTC REFUSAL LETTER")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(16)
                    .FontColor(Colors.Red.Darken2).ExtraBold();
                col.Item().Height(10);

                TwoColumnSection(col, "PAYMENT INFORMATION", new[]
                {
                    ("Filing date:", F(date)),
                    ("Payment rrr:", F(receipt.rrr)),
                    ("File number:", F(model.FileId)),
                    ("Fee title:", F(receipt.PaymentFor)),
                });

                var ctcRecordal = model.PostRegApplications?
                    .FirstOrDefault(p => p.RecordalType == "Trademark CTC Recordal" && p.Id == appId);

                col.Item().Element(Header).Text("REFUSAL INFORMATION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                
                var rejectionHistory = application.StatusHistory?
                    .FirstOrDefault(h => h.afterStatus == ApplicationStatuses.Rejected);

                FullWidthBox(col, "Reason for Refusal:", F(rejectionHistory?.Message ?? ctcRecordal?.Reason ?? "No reason provided"));
                FullWidthBox(col, "Decision Date:", F(DateTime.Now));

                // APPLICANT INFORMATION
                col.Item().Element(Header).Text("APPLICANT INFORMATION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                if (model.applicants != null && model.applicants.Count > 0)
                {
                    for (int i = 0; i < model.applicants.Count; i++)
                    {
                        var applicant = model.applicants[i];
                        var applicantNumber = GetApplicantNumberDisplay(i + 1);

                        TwoColumnSection(col, string.Empty, new[]
                        {
                            ($"{applicantNumber} APPLICANT:", F(applicant?.Name)),
                            ("Email:", F(applicant?.Email)),
                            ("Phone number:", F(applicant?.Phone)),
                            ("State:", F(applicant?.State)),
                            ("Address:", F(applicant?.Address)),
                            ("Nationality:", F(applicant?.country))
                        });
                    }
                }

                // TRADEMARK INFORMATION
                col.Item().Element(Header).Text("TRADEMARK INFORMATION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                FullWidthBox(col, "Title of Trademark:", F(model.TitleOfTradeMark));
                FullWidthBox(col, "Class:", F(model.TrademarkClass));
                FullWidthBox(col, "Registration Number:", F(model.RtmNumber));

                col.Item().AlignCenter().PaddingTop(30)
                    .Text("YOUR CTC REQUEST HAS BEEN REFUSED")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Red.Darken2);

                col.Item().AlignCenter().PaddingTop(10)
                    .Text("Please contact us for further clarification")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(10);
            });
        }

        private string GetApplicantNumberDisplay(int number)
        {
            return number switch
            {
                1 => "FIRST",
                2 => "SECOND",
                3 => "THIRD",
                4 => "FOURTH",
                5 => "FIFTH",
                _ => $"{number}TH"
            };
        }

        private static void TwoColumnSection(ColumnDescriptor col, string title, (string Label, string Value)[] pairs)
        {
            if (!string.IsNullOrWhiteSpace(title))
                col.Item().Element(Header).Text(title).FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

            for (int i = 0; i < pairs.Length; i += 2)
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
                    c2.Item().Text(label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                WriteText(c2.Item(), value);
            });
        }
    }
}
