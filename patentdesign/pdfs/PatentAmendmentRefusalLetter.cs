using patentdesign.Models;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System.Text.Json;

namespace patentdesign.pdfs
{
    public class PatentAmendmentRefusalLetter: IDocument
    {

        private readonly Filling model;
        private readonly string url;
        private readonly Receipt receipt;
        private readonly ApplicationInfo application;
        private readonly string appId;

        public PatentAmendmentRefusalLetter(Filling model, string url, Receipt receipt, ApplicationInfo application, string appId)
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

                // Header
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
                    .Text("PATENT AMENDMENT REFUSAL LETTER")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(16)
                    .FontColor(Colors.Black).ExtraBold();
                col.Item().Height(10);

                // PAYMENT INFORMATION
                TwoColumnSection(col, "PAYMENT INFORMATION", new[]
                {
                    ("Filing date:", F(date)),
                    ("Payment rrr:", F(receipt.rrr)),
                    ("File number:", F(model.FileId)),
                    ("Fee title:",   F(receipt.PaymentFor)),
                });

                // Assignment post-reg data
                var amendmentRecordal = model.PostRegApplications?
                    .FirstOrDefault(p => p.RecordalType == "Patent Amendment" && p.Id == appId);

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
                            ("Email:",        F(applicant?.Email)),
                            ("Phone number:", F(applicant?.Phone)),
                            ("State:",        F(applicant?.State)),
                            ("Address:",      F(applicant?.Address)),
                            ("Nationality:",  F(applicant?.country))
                        });
                    }
                }
                else
                {
                    TwoColumnSection(col, string.Empty, new[]
                    {
                        ("FIRST APPLICANT:", "N/A"),
                        ("Email:",        "N/A"),
                        ("Phone number:", "N/A"),
                        ("State:",        "N/A"),
                        ("Address:",      "N/A"),
                        ("Nationality:",  "N/A")
                    });
                }

                // PATENT INFORMATION
                col.Item().Element(Header)
                    .Text("PATENT INFORMATION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                FullWidthBox(col, "Title Of Invention:", F(model.TitleOfInvention));

                TwoColumnSection(col, string.Empty, new[]
                {
                     ("File Origin:", F(model.FileOrigin)),
                     ("Patent type:",      $"{F(model.PatentType)} - {F(model.FileOrigin)}")
                 });

                FullWidthBox(col, "Application Type:", F(model.PatentApplicationType));

                // REFUSAL INFORMATION
                col.Item().Element(Header)
                    .Text("REFUSAL INFORMATION")
                    .FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

                var refusalHistory = application.StatusHistory
                    .LastOrDefault(h => h.afterStatus == ApplicationStatuses.Rejected);

                var officerName = refusalHistory?.User ?? "-";
                var reason = refusalHistory?.Message ?? "-";

                TwoColumnSection(col, string.Empty, new[]
                {
                    ("Officer's Name:", officerName),
                    ("Reason:",       reason)
                });

                col.Item().AlignCenter().PaddingTop(30)
                    .Text("YOUR APPLICATION HAS BEEN REFUSED")
                    .FontFamily(Fonts.TimesNewRoman).Bold().FontColor(Colors.Black);
            });
        }

        private static void TwoColumnSection(ColumnDescriptor col, string title, (string Label, string Value)[] pairs)
        {
            if (!string.IsNullOrWhiteSpace(title))
            {
                col.Item().Element(Header)
                    .Text(title)
                    .FontFamily(Fonts.TimesNewRoman)
                    .FontSize(14)
                    .Bold();
            }

            for (var i = 0; i < pairs.Length; i += 2)
            {
                col.Item().Row(row =>
                {
                    row.RelativeItem().Element(Box).Column(c2 =>
                    {
                        c2.Item()
                            .Text(pairs[i].Label)
                            .FontFamily(Fonts.TimesNewRoman)
                            .FontSize(10)
                            .Bold();
                        WriteText(c2.Item(), pairs[i].Value);
                    });

                    if (i + 1 < pairs.Length)
                    {
                        row.RelativeItem().Element(Box).Column(c2 =>
                        {
                            c2.Item()
                                .Text(pairs[i + 1].Label)
                                .FontFamily(Fonts.TimesNewRoman)
                                .FontSize(10)
                                .Bold();
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
                    c2.Item()
                        .Text(label)
                        .FontFamily(Fonts.TimesNewRoman)
                        .FontSize(10)
                        .Bold();
                }

                WriteText(c2.Item(), value);
            });
        }

        private void DisplayAmendmentInformation(ColumnDescriptor col, PostRegistrationApp amendment)
        {
            col.Item().Element(Header).Text("AMENDED INFORMATION").FontFamily(Fonts.TimesNewRoman).FontSize(14).Bold();

            // Always show the amendment type
            FullWidthBox(col, "Amendment Type:", GetAmendmentTypeDisplay(amendment.AmendmentType));

            try
            {
                switch (amendment.AmendmentType)
                {
                    case "ApplicantName":
                        DisplayApplicantNameChanges(col, amendment);
                        break;
                    case "ApplicantAddress":
                        DisplayApplicantAddressChanges(col, amendment);
                        break;
                    case "FileTitle":
                        DisplayFileTitleChanges(col, amendment);
                        break;
                    case "CorrespondenceInformation":
                        DisplayCorrespondenceChanges(col, amendment);
                        break;
                    case "EditInventors":
                        DisplayInventorChanges(col, amendment);
                        break;
                    case "PriorityInfo":
                        DisplayPriorityChanges(col, amendment);
                        break;
                    case "AddAndRemoveApplicant":
                        DisplayApplicantManagementChanges(col, amendment);
                        break;
                    default:
                        FullWidthBox(col, "Changes:", "Amendment details were requested but rejected");
                        break;
                }
            }
            catch (Exception)
            {
                FullWidthBox(col, "Changes:", "Amendment was rejected - details unavailable");
            }
        }

        private string GetAmendmentTypeDisplay(string amendmentType)
        {
            return amendmentType switch
            {
                "ApplicantName" => "Applicant Name Amendment",
                "ApplicantAddress" => "Applicant Address Amendment", 
                "FileTitle" => "Patent Title & Abstract Amendment",
                "CorrespondenceInformation" => "Correspondence Information Amendment",
                "EditInventors" => "Inventor Information Amendment",
                "PriorityInfo" => "Priority Information Amendment",
                "AddAndRemoveApplicant" => "Add/Remove Applicants Amendment",
                _ => amendmentType ?? "Unknown Amendment Type"
            };
        }

        private void DisplayApplicantNameChanges(ColumnDescriptor col, PostRegistrationApp amendment)
        {
            if (!string.IsNullOrWhiteSpace(amendment.OldDataJson) && !string.IsNullOrWhiteSpace(amendment.NewDataJson))
            {
                try
                {
                    var oldNames = JsonSerializer.Deserialize<List<string>>(amendment.OldDataJson);
                    var newNames = JsonSerializer.Deserialize<List<string>>(amendment.NewDataJson);

                    FullWidthBox(col, "Requested Name Changes From:", string.Join(", ", oldNames ?? new List<string>()));
                    FullWidthBox(col, "Requested Name Changes To:", string.Join(", ", newNames ?? new List<string>()));
                }
                catch
                {
                    FullWidthBox(col, "Changes:", "Name amendment was requested but rejected");
                }
            }
        }

        private void DisplayApplicantAddressChanges(ColumnDescriptor col, PostRegistrationApp amendment)
        {
            if (!string.IsNullOrWhiteSpace(amendment.OldDataJson) && !string.IsNullOrWhiteSpace(amendment.NewDataJson))
            {
                try
                {
                    var oldData = JsonSerializer.Deserialize<JsonElement>(amendment.OldDataJson);
                    var newData = JsonSerializer.Deserialize<JsonElement>(amendment.NewDataJson);

                    if (oldData.TryGetProperty("Addresses", out var oldAddresses) && newData.TryGetProperty("Addresses", out var newAddresses))
                    {
                        var oldAddressList = JsonSerializer.Deserialize<List<string>>(oldAddresses.GetRawText());
                        var newAddressList = JsonSerializer.Deserialize<List<string>>(newAddresses.GetRawText());

                        FullWidthBox(col, "Current Addresses:", string.Join("; ", oldAddressList ?? new List<string>()));
                        FullWidthBox(col, "Requested Address Changes:", string.Join("; ", newAddressList ?? new List<string>()));
                    }

                    if (oldData.TryGetProperty("Emails", out var oldEmails) && newData.TryGetProperty("Emails", out var newEmails))
                    {
                        var oldEmailList = JsonSerializer.Deserialize<List<string>>(oldEmails.GetRawText());
                        var newEmailList = JsonSerializer.Deserialize<List<string>>(newEmails.GetRawText());

                        FullWidthBox(col, "Current Emails:", string.Join(", ", oldEmailList ?? new List<string>()));
                        FullWidthBox(col, "Requested Email Changes:", string.Join(", ", newEmailList ?? new List<string>()));
                    }
                }
                catch
                {
                    FullWidthBox(col, "Changes:", "Address amendment was requested but rejected");
                }
            }
        }

        private void DisplayFileTitleChanges(ColumnDescriptor col, PostRegistrationApp amendment)
        {
            if (!string.IsNullOrWhiteSpace(amendment.OldDataJson) && !string.IsNullOrWhiteSpace(amendment.NewDataJson))
            {
                try
                {
                    var oldData = JsonSerializer.Deserialize<JsonElement>(amendment.OldDataJson);
                    var newData = JsonSerializer.Deserialize<JsonElement>(amendment.NewDataJson);

                    if (oldData.TryGetProperty("Title", out var oldTitle) && newData.TryGetProperty("Title", out var newTitle))
                    {
                        FullWidthBox(col, "Current Title:", oldTitle.GetString() ?? "N/A");
                        FullWidthBox(col, "Requested Title Change:", newTitle.GetString() ?? "N/A");
                    }

                    if (oldData.TryGetProperty("Abstract", out var oldAbstract) && newData.TryGetProperty("Abstract", out var newAbstract))
                    {
                        FullWidthBox(col, "Current Abstract:", oldAbstract.GetString() ?? "N/A");
                        FullWidthBox(col, "Requested Abstract Change:", newAbstract.GetString() ?? "N/A");
                    }
                }
                catch
                {
                    FullWidthBox(col, "Changes:", "Title amendment was requested but rejected");
                }
            }
        }

        private void DisplayCorrespondenceChanges(ColumnDescriptor col, PostRegistrationApp amendment)
        {
            if (!string.IsNullOrWhiteSpace(amendment.OldDataJson) && !string.IsNullOrWhiteSpace(amendment.NewDataJson))
            {
                try
                {
                    var oldData = JsonSerializer.Deserialize<JsonElement>(amendment.OldDataJson);
                    var newData = JsonSerializer.Deserialize<JsonElement>(amendment.NewDataJson);

                    var fields = new[] { "Name", "Address", "Email", "Phone", "State", "Nationality" };

                    foreach (var field in fields)
                    {
                        if (oldData.TryGetProperty(field, out var oldValue) && newData.TryGetProperty(field, out var newValue))
                        {
                            var oldStr = oldValue.ValueKind == JsonValueKind.Null ? "N/A" : oldValue.GetString();
                            var newStr = newValue.ValueKind == JsonValueKind.Null ? "N/A" : newValue.GetString();

                            if (oldStr != newStr)
                            {
                                TwoColumnSection(col, string.Empty, new[]
                                {
                                    ($"Current {field}:", oldStr ?? "N/A"),
                                    ($"Requested {field}:", newStr ?? "N/A")
                                });
                            }
                        }
                    }
                }
                catch
                {
                    FullWidthBox(col, "Changes:", "Correspondence amendment was requested but rejected");
                }
            }
        }

        private void DisplayInventorChanges(ColumnDescriptor col, PostRegistrationApp amendment)
        {
            if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
            {
                try
                {
                    var newInventors = JsonSerializer.Deserialize<List<ApplicantInfo>>(amendment.NewDataJson);
                    FullWidthBox(col, "Requested Inventor Changes:", string.Join(", ", newInventors?.Select(i => i.Name) ?? new List<string>()));
                }
                catch
                {
                    FullWidthBox(col, "Changes:", "Inventor amendment was requested but rejected");
                }
            }
        }

        private void DisplayPriorityChanges(ColumnDescriptor col, PostRegistrationApp amendment)
        {
            if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
            {
                try
                {
                    var newData = JsonSerializer.Deserialize<JsonElement>(amendment.NewDataJson);

                    if (newData.TryGetProperty("FirstPriorityInfo", out var firstPriority))
                    {
                        var firstPriorityList = JsonSerializer.Deserialize<List<PriorityInfo>>(firstPriority.GetRawText());
                        if (firstPriorityList?.Any() == true)
                        {
                            FullWidthBox(col, "Requested First Priority:", string.Join(", ", firstPriorityList.Select(p => $"{p.number} ({p.Country})")));
                        }
                    }

                    if (newData.TryGetProperty("PriorityInfo", out var priority))
                    {
                        var priorityList = JsonSerializer.Deserialize<List<PriorityInfo>>(priority.GetRawText());
                        if (priorityList?.Any() == true)
                        {
                            FullWidthBox(col, "Requested Priority Claims:", string.Join(", ", priorityList.Select(p => $"{p.number} ({p.Country})")));
                        }
                    }
                }
                catch
                {
                    FullWidthBox(col, "Changes:", "Priority amendment was requested but rejected");
                }
            }
        }

        private void DisplayApplicantManagementChanges(ColumnDescriptor col, PostRegistrationApp amendment)
        {
            if (!string.IsNullOrWhiteSpace(amendment.NewDataJson))
            {
                try
                {
                    var newData = JsonSerializer.Deserialize<JsonElement>(amendment.NewDataJson);

                    if (newData.TryGetProperty("EditedApplicants", out var edited))
                    {
                        var editedList = JsonSerializer.Deserialize<List<ApplicantInfo>>(edited.GetRawText());
                        if (editedList?.Any() == true)
                        {
                            FullWidthBox(col, "Requested Applicant Edits:", string.Join(", ", editedList.Select(a => a.Name)));
                        }
                    }

                    if (newData.TryGetProperty("NewApplicants", out var newApps))
                    {
                        var newAppList = JsonSerializer.Deserialize<List<ApplicantInfo>>(newApps.GetRawText());
                        if (newAppList?.Any() == true)
                        {
                            FullWidthBox(col, "Requested New Applicants:", string.Join(", ", newAppList.Select(a => a.Name)));
                        }
                    }
                }
                catch
                {
                    FullWidthBox(col, "Changes:", "Applicant management amendment was requested but rejected");
                }
            }
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
                6 => "SIXTH",
                7 => "SEVENTH",
                8 => "EIGHTH", 
                9 => "NINTH",
                10 => "TENTH",
                _ => $"{number}TH"
            };
        }
    }
}

