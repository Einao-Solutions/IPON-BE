using patentdesign.Models;
using QRCoder;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace patentdesign.pdfs
{
    public class StatusSearchPdf(Filling file, List<byte[]>? images=null) : IDocument
    {
                
         public void Compose(IDocumentContainer container)
        {
            container.Page(page =>
            {
                page.Margin(30);
                page.Content().Element(ComposeContent);
                page.Footer().Row(row =>
                {
                    row.RelativeItem().Height(30).AlignRight(). Image("assets/logo.png").FitArea();
                });
            });
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
        static IContainer Block(IContainer container)
        {
            return container
                .Border(1)
                .ShowOnce()
                .MinWidth(50)
                .MinHeight(30)
                .AlignCenter()
                .AlignMiddle();
        }  
        static IContainer HeaderElement(IContainer container)
        {
            return container
                .Border(1)
                .Background(Colors.Grey.Lighten3)
                .ShowOnce()
                .MinWidth(50)
                .MinHeight(30)
                .AlignCenter()
                .AlignMiddle();
        }
        static IContainer AttachmentStyle(IContainer container)
        {
            return container
                .ShowOnce()
                .MaxWidth(80)
                // .MinWidth(50)
                // .MinHeight(30)
                .AlignCenter()
                .AlignMiddle();
        }
        void ComposeContent(IContainer container)
        {
            container
                .PaddingVertical(10)
                .Column(column =>
                {
                    column.Item().Height(60).AlignCenter().Image("assets/logo.png").FitArea();
                    column.Item().Height(20);
                    column.Item().AlignCenter().Text($"{file.Type.ToString().ToUpper()} STATUS REPORT")
                        .FontFamily(Fonts.TimesNewRoman).FontSize(18).Bold();
                    column.Item().Height(10);
                    column.Item().Padding(4).Text("File ID").Bold();
                    column.Item().Padding(4).Text(file.FileId);
                    if (file.RtmNumber != null)
                    {
                        column.Item().Padding(4).Text("RTM Number");
                        column.Item().Padding(4).Text(file.RtmNumber);
                    }

                    column.Item().Padding(4).Text("Title").Bold();
                    column.Item().Padding(4).Text(file.Type switch
                    {
                        FileTypes.TradeMark => file.TitleOfTradeMark,
                        FileTypes.Design => file.TitleOfDesign,
                        _ => file.TitleOfInvention
                    });
                    column.Item().Padding(4).Text("Application Date").Bold();
                    column.Item().Padding(4).Text(file.DateCreated.ToString("D"));
                    column.Item().Padding(4).Text("Applicants").Bold();
                    column.Item().Table(table =>
                    {
                        table.ColumnsDefinition(cols =>
                        {
                            cols.ConstantColumn(30);
                            cols.RelativeColumn();
                        });
                        foreach (var applicant in file.applicants)
                        {
                            table.Cell().Padding(4).Text($"{file.applicants.IndexOf(applicant) + 1}").Bold();
                            table.Cell().Padding(4).Column(ca =>
                            {
                                ca.Item().Text("Name").Bold();
                                ca.Item().Text(applicant.Name);
                                ca.Item().Text("Address").Bold();
                                ca.Item().Text(applicant.Address);
                                ca.Item().Text("Country").Bold();
                                ca.Item().Text(applicant.country);
                            });
                        }
                    });
                    if (file.Type == FileTypes.TradeMark)
                    {
                        column.Item().Padding(4).Text("TradeMark class").Bold();
                        column.Item().Padding(4).Text(file.TrademarkClass.ToString());
                        column.Item().Padding(4).Text("TradeMark Class Description").Bold();
                        column.Item().Padding(4).Text(file.TrademarkClassDescription);
                        column.Item().Padding(4).Text("TradeMark Type").Bold();
                        column.Item().Padding(4).Text(file.TrademarkType.ToString());
                        column.Item().Padding(4).Text("TradeMark Logo").Bold();
                        column.Item().Padding(4).Text(file.TrademarkLogo.ToString());
                        column.Item().Padding(4).Text("TradeMark Disclaimer").Bold();
                        column.Item().Padding(4).Text(file.TrademarkDisclaimer);
                    }

                    if (file.Type == FileTypes.Design)
                    {
                        column.Item().Padding(4).Text("Design Type").Bold();
                        column.Item().Padding(4).Text(file.DesignType.ToString());
                        column.Item().Padding(4).Text("Statement of Novelty").Bold();
                        column.Item().Padding(4).Text(file.StatementOfNovelty);
                        column.Item().Padding(4).Text("Design Creators").Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(30);
                                cols.RelativeColumn();
                            });
                            foreach (var applicant in file.DesignCreators)
                            {
                                table.Cell().Padding(4).Text($"{file.DesignCreators.IndexOf(applicant) + 1}").Bold();
                                table.Cell().Padding(4).Column(ca =>
                                {
                                    ca.Item().Text("Name").Bold();
                                    ca.Item().Text(applicant.Name);
                                    ca.Item().Text("Country").Bold();
                                    ca.Item().Text(applicant.country);
                                });
                            }
                        });
                    }

                    if (file.Type == FileTypes.Patent)
                    {
                        column.Item().Padding(4).Text("Patent Type").Bold();
                        column.Item().Padding(4).Text(file.PatentType.ToString());
                        column.Item().Padding(4).Text("Patent Abstract").Bold();
                        column.Item().Padding(4).Text(file.PatentAbstract);
                        column.Item().Padding(4).Text("Patent Application Type").Bold();
                        column.Item().Padding(4).Text(file.PatentApplicationType.ToString());
                        column.Item().Padding(4).Text("Patent Inventors").Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(30);
                                cols.RelativeColumn();
                            });
                            foreach (var applicant in file.Inventors)
                            {
                                table.Cell().Padding(4).Text($"{file.Inventors.IndexOf(applicant) + 1}").Bold();
                                table.Cell().Padding(4).Column(ca =>
                                {
                                    ca.Item().Text("Name").Bold();
                                    ca.Item().Text(applicant.Name);
                                    ca.Item().Text("Country").Bold();
                                    ca.Item().Text(applicant.country);
                                });
                            }
                        });
                    }

                    column.Item().Padding(4).Text("Correspondence Information").Bold();
                    column.Item().Padding(4).Text("Correspondence Name").Bold();
                    column.Item().Padding(4).Text(file.Correspondence?.name);
                    column.Item().Padding(4).Text("Correspondence Address").Bold();
                    column.Item().Padding(4).Text(file.Correspondence?.address);
                    column.Item().Padding(4).Text("Correspondence Email").Bold();
                    column.Item().Padding(4).Text(file.Correspondence?.email);
                    column.Item().Padding(4).Text("Correspondence Phone number").Bold();
                    column.Item().Padding(4).Text(file.Correspondence?.phone);
                    column.Item().Padding(4).Text("Correspondence State").Bold();
                    column.Item().Padding(4).Text(file.Correspondence?.state);
                    column.Item().Padding(4).Text("Representations").Bold();
                    if (images != null)
                    {
                        column.Item().Row(row =>
                        {
                            foreach (var image in images)
                            {
                                var img = Image.FromBinaryData(image);
                                row.RelativeItem().Height(100).Image(img).FitArea();
                            }
                        });
                    }

                    if (file.PriorityInfo.Count > 0)
                    {
                        column.Item().Padding(4).Text("Priority Information").Bold();
                        column.Item().Table(table =>
                        {
                            table.ColumnsDefinition(cols =>
                            {
                                cols.ConstantColumn(30);
                                cols.RelativeColumn();
                            });
                            foreach (var applicant in file.PriorityInfo)
                            {
                                table.Cell().Padding(4).Text($"{file.PriorityInfo.IndexOf(applicant) + 1}").Bold();
                                table.Cell().Padding(4).Column(ca =>
                                {
                                    ca.Item().Text("Country").Bold();
                                    ca.Item().Text(applicant.Country);
                                    ca.Item().Text("Priority Number").Bold();
                                    ca.Item().Text(applicant.number);
                                    ca.Item().Text("Priority Date").Bold();
                                    ca.Item().Text(applicant.Date);
                                });
                            }
                        });
                    }

                    column.Item().Padding(4).Text("Applications History").Bold();
                    foreach (var applicationInfo in file.ApplicationHistory)
                    {
                        if (applicationInfo.ApplicationType == FormApplicationTypes.NewApplication)
                        {
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(80);
                                    cols.RelativeColumn();
                                });

                                table.Cell().Padding(4).Text("Application Type").Bold();
                                table.Cell().Padding(4).Text("Fresh Application");
                                table.Cell().Padding(4).Text("Current Status").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.CurrentStatus.ToString());
                                table.Cell().Padding(4).Text("Payment ID").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.PaymentId);
                                if (applicationInfo.CertificatePaymentId != null)
                                {
                                    table.Cell().Padding(4).Text("Certificate payment ID").Bold();
                                    table.Cell().Padding(4).Text(applicationInfo.CertificatePaymentId);
                                }
                            });
                        }

                        if (applicationInfo.ApplicationType == FormApplicationTypes.LicenseRenewal)
                        {
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(30);
                                    cols.RelativeColumn();
                                });
                                table.Cell().Padding(4).Text("Application Type").Bold();
                                table.Cell().Padding(4).Text("Renewal Application");
                                table.Cell().Padding(4).Text("Current Status").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.CurrentStatus.ToString());
                                table.Cell().Padding(4).Text("Payment ID").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.PaymentId);
                            });
                        }

                        if (applicationInfo.ApplicationType == FormApplicationTypes.DataUpdate)
                        {
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(30);
                                    cols.RelativeColumn();
                                });
                                table.Cell().Padding(4).Text("Application Type").Bold();
                                table.Cell().Padding(4).Text("Data Update Application");
                                table.Cell().Padding(4).Text("Current Status").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.CurrentStatus.ToString());
                                if (applicationInfo.PaymentId != null)
                                {
                                    table.Cell().Padding(4).Text("Payment ID").Bold();
                                    table.Cell().Padding(4).Text(applicationInfo.PaymentId);
                                }

                                table.Cell().Padding(4).Text("Field to change").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.FieldToChange);
                                table.Cell().Padding(4).Text("Old Value").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.OldValue);
                                table.Cell().Padding(4).Text("New Value").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.NewValue);
                            });
                        }

                        if (applicationInfo.ApplicationType == FormApplicationTypes.Ownership)
                        {
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(30);
                                    cols.RelativeColumn();
                                });
                                table.Cell().Padding(4).Text("Application Type").Bold();
                                table.Cell().Padding(4).Text("Ownership Change");
                                table.Cell().Padding(4).Text("Current Status").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.CurrentStatus.ToString());
                                table.Cell().Padding(4).Text("Previous owner").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.StatusHistory[0].Message);
                            });
                        }

                        if (applicationInfo.ApplicationType == FormApplicationTypes.Assignment)
                        {
                            column.Item().Table(table =>
                            {
                                table.ColumnsDefinition(cols =>
                                {
                                    cols.ConstantColumn(30);
                                    cols.RelativeColumn();
                                });
                                table.Cell().Padding(4).Text("Application Type").Bold();
                                table.Cell().Padding(4).Text("Assignment Application");
                                table.Cell().Padding(4).Text("Current Status").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.CurrentStatus.ToString());
                                table.Cell().Padding(4).Text("Assignee Name").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.Assignment.assigneeName);
                                table.Cell().Padding(4).Text("Assignee Address").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.Assignment.assigneeAddress);
                                table.Cell().Padding(4).Text("Assignee Country").Bold();
                                table.Cell().Padding(4).Text(applicationInfo.Assignment.assigneeCountry);
                            });
                        }
                    }
                });
        }
        private void GetQrCode(IContainer container)
        {
            using (QRCodeGenerator qrGenerator = new QRCodeGenerator())
            using (QRCodeData qrCodeData = qrGenerator.CreateQrCode("url", QRCodeGenerator.ECCLevel.Q))
            using (PngByteQRCode qrCode = new PngByteQRCode(qrCodeData))
            {
                byte[] qrCodeImage = qrCode.GetGraphic(20);
                container.Image(qrCodeImage).FitArea();
            }
        }
    }
}