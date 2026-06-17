using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;

namespace patentdesign.pdfs
{
    public class DesignAmendmentAcknowledgementLetter : IDocument
    {
        private readonly Filling model;
        private readonly string url;
        private readonly Receipt receipt;
        private readonly string appId;

        public DesignAmendmentAcknowledgementLetter(Filling model, string url, Receipt receipt, string appId)
        {
            this.model = model;
            this.url = url;
            this.receipt = receipt;
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

                // Header
                col.Item().Height(60).AlignCenter().PaddingBottom(10).Image("assets/logo.png").FitArea();
                col.Item().AlignCenter().PaddingBottom(10).Text("FEDERAL REPUBLIC OF NIGERIA").FontFamily(Fonts.TimesNewRoman).FontSize(20).Bold();
                col.Item().AlignCenter().Text("FEDERAL MINISTRY OF INDUSTRY, TRADE AND INVESTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().PaddingBottom(10).Text("COMMERCIAL LAW DEPARTMENT").FontFamily(Fonts.TimesNewRoman).FontSize(14);
                col.Item().AlignCenter().Text("DESIGN AMENDMENT ACKNOWLEDGEMENT LETTER").FontFamily(Fonts.TimesNewRoman).FontSize(16).FontColor(Colors.Green.Darken3).ExtraBold();
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
                    TwoColumnSection(col, "AMENDED INFORMATION", new[]
                    {
                        ("Update Type:", "N/A"),
                        ("Status:", "Amendment record not found"),
                    });
                }

                // APPLICANT INFORMATION
                col.Item().Element(Header).Text("APPLICANT INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
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

                // DESIGN INFORMATION
                col.Item().Element(Header).Text("DESIGN INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
                FullWidthBox(col, "Title of Industrial Design:", F(model.TitleOfDesign));
                FullWidthBox(col, "Design Type:", F(model.DesignType));
                FullWidthBox(col, "Statement of Novelty:", F(model.StatementOfNovelty));

                // Footer
                col.Item().AlignCenter().PaddingTop(30).Text("YOUR APPLICATION HAS BEEN RECEIVED AND IS RECEIVING DUE ATTENTION")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Green.Darken2);
            });
        }

        private void DisplayAmendmentInformation(ColumnDescriptor col, PostRegistrationApp amendment)
        {
            col.Item().Element(Header).Text("AMENDED INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();
            FullWidthBox(col, "Update Type:", GetAmendmentTypeDisplay(amendment.AmendmentType));

            var (oldValue, newValue) = GetOldAndNewValues(amendment);
            TwoColumnSection(col, string.Empty, new[]
            {
                ("Old:", oldValue),
                ("New:", newValue)
            });
        }

        private (string Old, string New) GetOldAndNewValues(PostRegistrationApp amendment)
        {
            try
            {
                switch (amendment.AmendmentType)
                {
                    case "ApplicantName":
                        var oldNames = model.applicants?.Select(a => a.Name).ToList() ?? new List<string>();
                        var newNames = JsonSerializer.Deserialize<List<string>>(amendment.NewDataJson);
                        return (string.Join(", ", oldNames), string.Join(", ", newNames ?? new List<string>()));

                    case "ApplicantAddress":
                        var oldAddresses = model.applicants?.Select(a => a.Address).ToList() ?? new List<string>();
                        return (string.Join(", ", oldAddresses), "Updated address information");

                    case "DesignTitle":
                        var newData = JsonSerializer.Deserialize<JsonElement>(amendment.NewDataJson);
                        var newTitle = newData.GetProperty("Title").GetString();
                        return (F(model.TitleOfDesign), F(newTitle));

                    case "DesignType":
                        var newDesignType = JsonSerializer.Deserialize<string>(amendment.NewDataJson);
                        return (F(model.DesignType), F(newDesignType));

                    case "StatementOfNovelty":
                        var newNovelty = JsonSerializer.Deserialize<string>(amendment.NewDataJson);
                        return (F(model.StatementOfNovelty), F(newNovelty));

                    case "CorrespondenceInformation":
                        return ("Existing correspondence details", "Updated correspondence information");

                    case "PriorityInfo":
                        return ("Existing priority claims", "Updated priority information");

                    case "AddAndRemoveApplicant":
                        return ("Current applicants", "Modified applicant list");

                    case "CreatorInformation":
                        var oldCreators = model.DesignCreators?.Select(c => c.Name).ToList() ?? new List<string>();
                        return (string.Join(", ", oldCreators), "Updated creator information");

                    case "DesignAttachments":
                        return ("Existing attachments", "Updated attachment files");

                    default:
                        return ("N/A", amendment.message ?? "Amendment requested");
                }
            }
            catch
            {
                return ("N/A", amendment.message ?? "Amendment requested");
            }
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
                "CreatorInformation" => "Creator Information Amendment",
                "DesignAttachments" => "Design Attachments Amendment",
                _ => amendmentType ?? "Unknown Amendment Type"
            };
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
