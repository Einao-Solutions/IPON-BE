using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class DesignRenewalAcknowledgementLetter : IDocument
    {
        private readonly Filling model;
        private readonly string appId;
        private readonly Receipt receipt;

        public DesignRenewalAcknowledgementLetter(Filling model, string appId, Receipt receipt)
        {
            this.model = model;
            this.appId = appId;
            this.receipt = receipt;
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
            bool placeholder = text == "N/A";
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
                string date = "-";
                if (!string.IsNullOrWhiteSpace(receipt.Date) && DateTime.TryParse(receipt.Date, out var parsedDate))
                    date = parsedDate.ToString("dd/MM/yyyy");

                var renewals = model.ApplicationHistory
                    .Where(a => a.ApplicationType == FormApplicationTypes.LicenseRenewal)
                    .OrderBy(a => a.ApplicationDate)
                    .ToList();
                var current = renewals.FirstOrDefault(a => a.id == appId);
                var index = current == null ? -1 : renewals.IndexOf(current);
                DateOnly? dueDate = null;
                if (index > 0)
                    dueDate = renewals[index - 1].ExpiryDate;
                else if (model.ApplicationHistory.Count > 0)
                    dueDate = model.ApplicationHistory[0].ExpiryDate;
                DateOnly? nextRenewalDate = current?.ExpiryDate;

                col.Item().Height(60).AlignCenter().PaddingBottom(10).Image("assets/logo.png").FitArea();
                col.Item().AlignCenter().PaddingBottom(10).Text("FEDERAL REPUBLIC OF NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                col.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().PaddingBottom(10).Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().Text("DESIGN RENEWAL ACKNOWLEDGEMENT LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(16).FontColor(Colors.Green.Darken3).ExtraBold();
                col.Item().Height(10);

                TwoColumnSection(col, "PAYMENT INFORMATION", new[]
                {
                    ("Payment date:", F(date)),
                    ("Payment ID:", F(receipt.rrr)),
                    ("File number:", F(model.FileId)),
                    ("Fee title:", F(receipt.PaymentFor)),
                });

                TwoColumnSection(col, "RENEWAL INFORMATION", new[]
                {
                    ("Renewal Due Date:", F(dueDate?.ToString("dd MMMM, yyyy"))),
                    ("Next Renewal Date:", F(nextRenewalDate?.ToString("dd MMMM, yyyy"))),
                });

                col.Item().Element(Header).Text("DESIGN INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                FullWidthBox(col, "Title of Industrial Design:", F(model.TitleOfDesign));
                FullWidthBox(col, "Design Type:", F(model.DesignType));
                FullWidthBox(col, "Statement of Novelty:", F(model.StatementOfNovelty));

                col.Item().Element(Header).Text("APPLICANT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                if (model.applicants != null && model.applicants.Count > 0)
                {
                    foreach (var applicant in model.applicants)
                    {
                        TwoColumnSection(col, string.Empty, new[]
                        {
                            ("Name:", F(applicant?.Name)),
                            ("Email:", F(applicant?.Email)),
                            ("Phone number:", F(applicant?.Phone)),
                            ("State:", F(applicant?.State)),
                            ("Address:", F(applicant?.Address)),
                            ("Nationality:", F(applicant?.country))
                        });
                    }
                }

                col.Item().Element(Header).Text("CORRESPONDENCE INFORMATION (ADDRESS OF SERVICE)").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Name:", F(model.Correspondence?.name)),
                    ("Address:", F(model.Correspondence?.address)),
                    ("Email:", F(model.Correspondence?.email)),
                    ("Phone number:", F(model.Correspondence?.phone))
                });

                col.Item().AlignCenter().PaddingTop(30).Text("YOUR APPLICATION HAS BEEN RECEIVED AND IS RECEIVING DUE ATTENTION")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Green.Darken2);
            });
        }

        private static void TwoColumnSection(ColumnDescriptor col, string title, (string Label, string Value)[] pairs)
        {
            if (!string.IsNullOrWhiteSpace(title))
                col.Item().Element(Header).Text(title).FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

            for (int i = 0; i < pairs.Length; i += 2)
            {
                int idx = i;
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(Box).Column(c2 =>
                    {
                        c2.Item().Text(pairs[idx].Label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                        WriteText(c2.Item(), pairs[idx].Value);
                    });

                    if (idx + 1 < pairs.Length)
                    {
                        row.RelativeItem().Element(Box).Column(c2 =>
                        {
                            c2.Item().Text(pairs[idx + 1].Label).FontFamily(Fonts.TimesNewRoman).FontSize(10).Bold();
                            WriteText(c2.Item(), pairs[idx + 1].Value);
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
